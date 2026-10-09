using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Kitopia.Desktop.Features.Search;
using Kitopia.Desktop.Features.Search.Semantic;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Ocr;
using Pinyin.NET;
using Serilog;

namespace Kitopia.Desktop.Features.Indexing;

/// <summary>
/// The process-wide owner of lexical, text semantic, and image semantic indexes.
/// It starts from source entries and uses index.db; legacy search-rag.db is intentionally ignored.
/// </summary>
public sealed class IndexService : IIndexService, IDisposable
{
    private const int PinyinResultLimit = 100;
    private const int SemanticFallbackPinyinResultLimit = 10;
    private const int MinimumSemanticQueryLength = 2;
    private const int ManagedFileSearchBatchSize = 256;
    private const int MaximumOcrInputCharacters = 16 * 1024;
    private const int ImageInferenceBatchSize = 8;
    private static readonly ILogger Logger = LogManager.Logger.ForContext<IndexService>();
    private static readonly StringComparer EntryKeyComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    // Windows file systems are case-insensitive. Keeping file keys case-sensitive causes
    // Everything to produce a second entry when it changes the casing of a returned path.
    private readonly Dictionary<string, IndexedEntry> _entries = new(EntryKeyComparer);
    private readonly object _entriesLock = new();
    private readonly IndexVectorStore _store = new();
    private readonly SemaphoreSlim _rebuildGate = new(1, 1);
    private readonly SemaphoreSlim _pinyinBuildGate = new(1, 1);
    private PinyinSearcher<KeyValuePair<string, string>>? _pinyinSearcher;
    private int _pinyinRebuildVersion;
    private int _pinyinRebuildQueued;
    private int _statusPublishQueued;
    private int _statusPublishVersion;
    private EmbeddingGemmaEmbeddingService? _embeddingService;
    private readonly IOcrService? _ocrService;
    private IndexStatusSnapshot _status = IndexStatusSnapshot.Empty;
    private readonly object _operationStateLock = new();
    private CancellationTokenSource? _activeOperationCancellation;
    private CancellationTokenSource? _activeStepCancellation;
    private TaskCompletionSource<bool>? _resumeSignal;
    private bool _isPaused;
    private bool _isForegroundPaused;

    public event EventHandler<IndexStatusSnapshot>? StatusChanged;

    bool ISearchEntryIndex.TryAdd(SearchEntry entry) => TryAdd(entry);

    public IndexService(IOcrService? ocrService = null)
    {
        _ocrService = ocrService;
    }

    public IndexStatusSnapshot GetStatus() => Volatile.Read(ref _status);

    public void PauseIndexing() => SetPauseState(true, foreground: false);

    public void ResumeIndexing() => SetPauseState(false, foreground: false);

    public void SetForegroundPause(bool paused) => SetPauseState(paused, foreground: true);

    public void CancelIndexing()
    {
        CancellationTokenSource? cancellation;
        lock (_operationStateLock)
        {
            cancellation = _activeOperationCancellation;
        }

        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The operation completed while the cancellation request was being issued.
        }
        SetPauseState(false, foreground: false);
    }

    private void SetPauseState(bool paused, bool foreground)
    {
        TaskCompletionSource<bool>? resumeSignal = null;
        CancellationTokenSource? stepCancellation = null;
        bool effectivePause;
        lock (_operationStateLock)
        {
            if (foreground)
            {
                if (_isForegroundPaused == paused) return;
                _isForegroundPaused = paused;
            }
            else
            {
                if (_isPaused == paused || (paused && _activeOperationCancellation is null)) return;
                _isPaused = paused;
            }

            effectivePause = _isPaused || _isForegroundPaused;
            if (effectivePause && _resumeSignal is null)
            {
                _resumeSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                stepCancellation = _activeStepCancellation;
            }
            else if (!effectivePause && _resumeSignal is not null)
            {
                resumeSignal = _resumeSignal;
                _resumeSignal = null;
            }
        }

        try { stepCancellation?.Cancel(); }
        catch (ObjectDisposedException) { }
        resumeSignal?.TrySetResult(true);
        if (paused && !foreground)
            _ = Task.Run(ReleaseIndexingSessionsAsync);
        if (GetStatus().IsRebuilding)
        {
            UpdateStatus(status => status with { IsPaused = effectivePause });
        }
    }

    public bool TryAdd(SearchEntry entry, IndexSource source = IndexSource.Application)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.OnlyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.DisplayName);
        if (IsIgnoredPath(entry.OnlyKey))
        {
            return false;
        }

        var changed = false;
        lock (_entriesLock)
        {
            if (_entries.TryGetValue(entry.OnlyKey, out var existing)
                && existing.Entry.Equals(entry)
                && existing.Source == source)
            {
                return false;
            }

            _entries[entry.OnlyKey] = new IndexedEntry(entry, source);
            changed = true;
        }

        if (changed)
        {
            PublishStatus();
        }

        return changed;
    }

    public bool TryRemove(string onlyKey)
    {
        IndexedEntry? removedEntry = null;
        lock (_entriesLock)
        {
            if (_entries.Remove(onlyKey, out var existing))
            {
                removedEntry = existing;
            }
        }

        if (removedEntry is null) return false;
        DeleteVectorsInBackground([onlyKey]);
        RebuildPinyinSearcher();
        PublishStatus();
        return true;
    }

    public async Task RemoveIgnoredEntriesAsync(CancellationToken cancellationToken = default)
    {
        var ignoredPaths = GetIgnoredPathSnapshot();
        RemoveWhere((path, _) => IsIgnoredPath(path, ignoredPaths));
        if (ignoredPaths.Count == 0)
        {
            return;
        }

        await _rebuildGate.WaitAsync(cancellationToken);
        try
        {
            await _store.RemoveMatchingPathsAsync(
                path => IsIgnoredPath(path, ignoredPaths), cancellationToken);
        }
        finally
        {
            _rebuildGate.Release();
        }

        PublishStatus();
    }

    public bool TryGetValue(string onlyKey, out SearchEntry entry)
    {
        entry = default;
        if (IsIgnoredPath(onlyKey))
        {
            return false;
        }

        lock (_entriesLock)
        {
            if (_entries.TryGetValue(onlyKey, out var indexed))
            {
                entry = indexed.Entry;
                return true;
            }

        }

        if (TryGetFileFingerprint(onlyKey) is null)
        {
            return false;
        }

        return _store.ContainsManagedFilePath(onlyKey)
               && TryCreateFileEntry(onlyKey, out entry);
    }

    public bool ContainsKey(string onlyKey)
    {
        if (IsIgnoredPath(onlyKey))
        {
            return false;
        }

        lock (_entriesLock)
        {
            return _entries.ContainsKey(onlyKey);
        }
    }

    public int RemoveWhere(Func<string, SearchEntry, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        List<string> keys;
        lock (_entriesLock)
        {
            keys = _entries
                .Where(pair => predicate(pair.Key, pair.Value.Entry))
                .Select(pair => pair.Key)
                .ToList();
        }

        if (keys.Count == 0) return 0;
        lock (_entriesLock)
        {
            foreach (var key in keys)
            {
                _entries.Remove(key);
            }
        }

        DeleteVectorsInBackground(keys);
        RebuildPinyinSearcher();
        PublishStatus();

        return keys.Count;
    }

    public void Synchronize(IEnumerable<SearchEntry> entries, IndexSource source = IndexSource.Application)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var ignoredPaths = GetIgnoredPathSnapshot();
        var incomingByKey = new Dictionary<string, SearchEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (IsIgnoredPath(entry.OnlyKey, ignoredPaths))
            {
                continue;
            }

            incomingByKey[entry.OnlyKey] = entry;
        }

        List<string> removed;
        var changed = false;
        lock (_entriesLock)
        {
            removed = _entries.Where(pair => pair.Value.Source == source && !incomingByKey.ContainsKey(pair.Key))
                .Select(pair => pair.Key)
                .ToList();
            foreach (var key in removed)
            {
                _entries.Remove(key);
                changed = true;
            }

            foreach (var (key, entry) in incomingByKey)
            {
                if (_entries.TryGetValue(key, out var existing)
                    && existing.Source == source
                    && existing.Entry.Equals(entry))
                {
                    continue;
                }

                _entries[key] = new IndexedEntry(entry, source);
                changed = true;
            }
        }

        if (removed.Count > 0)
        {
            DeleteVectorsInBackground(removed);
        }

        if (changed)
        {
            RebuildPinyinSearcher();
            PublishStatus();
        }
    }

    public async Task<bool> SynchronizeFilesAsync(
        IEnumerable<string> paths,
        IndexSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var ignoredPaths = GetIgnoredPathSnapshot();
        HashSet<string> protectedKeys;
        lock (_entriesLock)
        {
            protectedKeys = _entries.Keys
                .Where(path => !IsIgnoredPath(path, ignoredPaths))
                .ToHashSet(EntryKeyComparer);
        }

        var changed = await _store.SynchronizeFileSourceAsync(
            source,
            paths.Where(path => !IsIgnoredPath(path, ignoredPaths)),
            protectedKeys,
            cancellationToken);
        if (changed)
        {
            Interlocked.Increment(ref _pinyinRebuildVersion);
            PublishStatus();
        }

        return changed;
    }

    public void RebuildPinyinSearcher()
    {
        Interlocked.Increment(ref _pinyinRebuildVersion);
        if (Interlocked.Exchange(ref _pinyinRebuildQueued, 1) != 0)
        {
            return;
        }

        var rebuildTask = Task.Run(async () =>
        {
            while (true)
            {
                var targetVersion = Volatile.Read(ref _pinyinRebuildVersion);
                await BuildPinyinSearcherAsync(targetVersion, CancellationToken.None);
                if (Volatile.Read(ref _pinyinRebuildVersion) == targetVersion)
                {
                    Volatile.Write(ref _pinyinRebuildQueued, 0);
                    if (Volatile.Read(ref _pinyinRebuildVersion) == targetVersion)
                    {
                        return;
                    }

                    if (Interlocked.Exchange(ref _pinyinRebuildQueued, 1) != 0)
                    {
                        return;
                    }
                }
            }
        });
        _ = rebuildTask.ContinueWith(
            task => Logger.Warning(task.Exception, "Pinyin index rebuild failed."),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    public Task RebuildPinyinSearcherAsync(CancellationToken cancellationToken = default)
    {
        var version = Interlocked.Increment(ref _pinyinRebuildVersion);
        return BuildPinyinSearcherAsync(version, cancellationToken);
    }

    private async Task BuildPinyinSearcherAsync(int version, CancellationToken cancellationToken)
    {
        await _pinyinBuildGate.WaitAsync(cancellationToken);
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Managed files are searched from SQLite in bounded pages. Keeping them in this
                // cache creates one PinyinToken graph per file and duplicates it during rebuilds.
                var searcher = new PinyinSearcher<KeyValuePair<string, string>>(EnumeratePinyinEntries(), entry => entry.Value);
                cancellationToken.ThrowIfCancellationRequested();
                if (Volatile.Read(ref _pinyinRebuildVersion) == version)
                {
                    _pinyinSearcher = searcher;
                }
            }, cancellationToken);
        }
        finally
        {
            _pinyinBuildGate.Release();
        }
    }

    private IEnumerable<KeyValuePair<string, string>> EnumeratePinyinEntries()
    {
        List<KeyValuePair<string, string>> entries;
        lock (_entriesLock)
        {
            entries = new List<KeyValuePair<string, string>>(_entries.Count);
            foreach (var indexed in _entries.Values)
            {
                entries.Add(new KeyValuePair<string, string>(indexed.Entry.OnlyKey, indexed.Entry.DisplayName));
            }
        }

        foreach (var entry in entries)
        {
            yield return entry;
        }

    }

    public IReadOnlyList<KeyValuePair<string, SearchEntry>> GetEntriesSnapshot()
    {
        var ignoredPaths = GetIgnoredPathSnapshot();
        lock (_entriesLock)
        {
            return _entries
                .Where(pair => !IsIgnoredPath(pair.Key, ignoredPaths))
                .Select(pair => new KeyValuePair<string, SearchEntry>(pair.Key, pair.Value.Entry))
                .ToList();
        }
    }

    public IReadOnlyList<SearchIndexResult> SearchPinyin(
        string query,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || maximumResults <= 0) return [];
        cancellationToken.ThrowIfCancellationRequested();
        return SearchPinyinResults(query, maximumResults, cancellationToken);
    }

    public async Task<IReadOnlyList<SearchIndexResult>> SearchAsync(
        string query,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || maximumResults <= 0) return [];
        cancellationToken.ThrowIfCancellationRequested();
        var ignoredPaths = GetIgnoredPathSnapshot();
        var pinyinResults = SearchPinyinResults(query, PinyinResultLimit, cancellationToken);
        var merged = new Dictionary<string, SearchIndexResult>(EntryKeyComparer);
        for (var index = 0; index < pinyinResults.Count; index++)
        {
            var result = pinyinResults[index];
            merged[result.Source.OnlyKey] = result with { Weight = 1d / (60 + index + 1) };
        }

        if (!ShouldSearchSemantically(query, pinyinResults.Count))
        {
            return merged.Values.OrderByDescending(result => result.Weight).Take(maximumResults).ToList();
        }

        var semanticResults = await SearchSemanticAsync(query, maximumResults, cancellationToken);
        foreach (var match in semanticResults.SelectMany(matches => matches))
        {
            if (IsIgnoredPath(match.Key, ignoredPaths))
            {
                continue;
            }

            if (!TryGetValue(match.Key, out var entry)) continue;
            var score = Math.Max(0d, match.Score) / (60 + match.Rank + 1);
            if (merged.TryGetValue(match.Key, out var existing))
            {
                merged[match.Key] = existing with { Weight = existing.Weight + score };
            }
            else
            {
                merged[match.Key] = new SearchIndexResult(entry, score, null);
            }
        }

        return merged.Values.OrderByDescending(result => result.Weight).Take(maximumResults).ToList();
    }

    private IReadOnlyList<SearchIndexResult> SearchPinyinResults(
        string query,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        var ignoredPaths = GetIgnoredPathSnapshot();
        var candidateLimit = maximumResults > int.MaxValue / 4
            ? int.MaxValue
            : maximumResults * 4;
        var matches = new Dictionary<string, PinyinMatch>(EntryKeyComparer);
        var explicitKeys = new HashSet<string>(EntryKeyComparer);
        lock (_entriesLock)
        {
            foreach (var key in _entries.Keys)
            {
                explicitKeys.Add(key);
            }
        }

        AddExplicitPinyinMatches(
            matches,
            _pinyinSearcher?.Search(query, candidateLimit, cancellationToken),
            candidateLimit,
            explicitKeys);

        var batch = new List<KeyValuePair<string, string>>(ManagedFileSearchBatchSize);
        try
        {
            foreach (var path in _store.EnumerateManagedFilePaths(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsIgnoredPath(path, ignoredPaths)
                    || explicitKeys.Contains(path)
                    || !TryGetManagedFileDisplayName(path, out var displayName))
                {
                    continue;
                }

                batch.Add(new KeyValuePair<string, string>(path, displayName));
                if (batch.Count < ManagedFileSearchBatchSize)
                {
                    continue;
                }

                SearchManagedFileBatch(query, batch, matches, candidateLimit, cancellationToken);
                batch.Clear();
            }

            if (batch.Count > 0)
            {
                SearchManagedFileBatch(query, batch, matches, candidateLimit, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A temporary database/read failure must not make the interactive search fail. The
            // application/plugin snapshot collected above is still a valid result set.
            Logger.Debug(exception, "Managed-file pinyin search could not read index.db.");
        }

        var results = new List<SearchIndexResult>(maximumResults);
        foreach (var match in matches.Values
            .OrderByDescending(match => match.Weight)
            .ThenBy(match => match.Key.Length)
            .ThenBy(match => match.Key, EntryKeyComparer))
        {
            SearchEntry entry;
            if (match.Entry is { } explicitEntry)
            {
                entry = explicitEntry;
            }
            else if (!TryGetManagedFileEntry(match.Key, out entry))
            {
                continue;
            }

            results.Add(new SearchIndexResult(
                entry,
                1d / (60 + results.Count + 1),
                match.CharMatchResults));
            if (results.Count == maximumResults)
            {
                break;
            }
        }

        return results;
    }

    private void SearchManagedFileBatch(
        string query,
        IReadOnlyList<KeyValuePair<string, string>> batch,
        Dictionary<string, PinyinMatch> matches,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        var searcher = new PinyinSearcher<KeyValuePair<string, string>>(batch, entry => entry.Value);
        foreach (var result in searcher.Search(query, maximumResults, cancellationToken))
        {
            var candidate = new PinyinMatch(
                result.Source.Key,
                null,
                result.Weight,
                result.CharMatchResults);
            AddPinyinMatch(matches, candidate, maximumResults);
        }
    }

    private void AddExplicitPinyinMatches(
        Dictionary<string, PinyinMatch> matches,
        IReadOnlyList<SearchResults<KeyValuePair<string, string>>>? results,
        int maximumResults,
        IReadOnlySet<string> explicitKeys)
    {
        if (results is null)
        {
            return;
        }

        foreach (var result in results)
        {
            if (!explicitKeys.Contains(result.Source.Key))
            {
                continue;
            }

            if (!TryGetValue(result.Source.Key, out var entry))
            {
                continue;
            }

            AddPinyinMatch(
                matches,
                new PinyinMatch(
                    result.Source.Key,
                    entry,
                    result.Weight,
                    result.CharMatchResults),
                maximumResults);
        }
    }

    private static void AddPinyinMatch(
        Dictionary<string, PinyinMatch> matches,
        PinyinMatch candidate,
        int maximumResults)
    {
        if (matches.TryGetValue(candidate.Key, out var current)
            && current.Weight >= candidate.Weight)
        {
            return;
        }

        matches[candidate.Key] = candidate;
        if (matches.Count <= maximumResults)
        {
            return;
        }

        PinyinMatch? worst = null;
        foreach (var match in matches.Values)
        {
            if (worst is null || IsWorsePinyinMatch(match, worst))
            {
                worst = match;
            }
        }

        matches.Remove(worst!.Key);
    }

    private static bool IsWorsePinyinMatch(PinyinMatch candidate, PinyinMatch currentWorst)
    {
        var comparison = candidate.Weight.CompareTo(currentWorst.Weight);
        if (comparison != 0)
        {
            return comparison < 0;
        }

        comparison = candidate.Key.Length.CompareTo(currentWorst.Key.Length);
        if (comparison != 0)
        {
            return comparison > 0;
        }

        return EntryKeyComparer.Compare(candidate.Key, currentWorst.Key) > 0;
    }

    public Task IndexIncrementalAsync(IndexRebuildScope scope, CancellationToken cancellationToken = default) =>
        RunIndexingAsync(scope, rebuild: false, cancellationToken);

    public Task RebuildAsync(IndexRebuildScope scope, CancellationToken cancellationToken = default) =>
        RunIndexingAsync(scope, rebuild: true, cancellationToken);

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await _rebuildGate.WaitAsync(cancellationToken);
        var operationCancellation = BeginOperation(cancellationToken);
        try
        {
            var operationToken = operationCancellation.Token;
            UpdateStatus(status => status with
            {
                IsRebuilding = true,
                IsPaused = IsPauseRequested,
                FailedImages = 0,
                ProcessingImages = 0,
                TotalFileItems = 0,
                CompletedFileItems = 0,
                CurrentOperation = "lang.kitopia.clearing_file_index",
                CurrentItem = null,
                LastError = null
            });
            await WaitIfPausedAsync(operationToken);
            await RunPausableStepAsync(_store.ResetAsync, operationToken);
        }
        catch (OperationCanceledException) when (operationCancellation.IsCancellationRequested)
        {
            Logger.Information("Index reset was cancelled.");
        }
        catch (Exception exception)
        {
            UpdateStatus(status => status with { LastError = exception.Message });
            throw;
        }
        finally
        {
            FinishOperation(operationCancellation);
            UpdateStatus(status => status with
            {
                IsRebuilding = false,
                IsPaused = false,
                CurrentOperation = null,
                CurrentItem = null
            });
            _rebuildGate.Release();
            PublishStatus();
        }
    }

    private async Task RunIndexingAsync(
        IndexRebuildScope scope,
        bool rebuild,
        CancellationToken cancellationToken)
    {
        await _rebuildGate.WaitAsync(cancellationToken);
        var indexDocuments = scope is IndexRebuildScope.All or IndexRebuildScope.Documents or IndexRebuildScope.Files;
        var indexImages = scope is IndexRebuildScope.All or IndexRebuildScope.Images or IndexRebuildScope.Files;
        var operationCancellation = BeginOperation(cancellationToken);
        try
        {
            var operationToken = operationCancellation.Token;
            UpdateStatus(status => status with
            {
                IsRebuilding = true,
                IsPaused = IsPauseRequested,
                TotalFileItems = 0,
                CompletedFileItems = 0,
                CurrentOperation = rebuild ? "lang.kitopia.preparing_to_rebuild_indexes" : "lang.kitopia.preparing_to_update_indexes",
                CurrentItem = null,
                LastError = null
            });

            if (scope is IndexRebuildScope.All or IndexRebuildScope.Pinyin)
            {
                await WaitIfPausedAsync(operationToken);
                UpdateStatus(status => status with { CurrentOperation = "lang.kitopia.rebuilding_pinyin_index", CurrentItem = null });
                await RunPausableStepAsync(RebuildPinyinSearcherAsync, operationToken);
            }

            if (rebuild && indexDocuments)
            {
                await WaitIfPausedAsync(operationToken);
                UpdateStatus(status => status with { CurrentOperation = "lang.kitopia.clearing_text_index", CurrentItem = null });
                await RunPausableStepAsync(
                    token => _store.ClearAsync(IndexRebuildScope.Documents, token), operationToken);
            }

            if (rebuild && indexImages)
            {
                await WaitIfPausedAsync(operationToken);
                UpdateStatus(status => status with { CurrentOperation = "lang.kitopia.clearing_image_index", CurrentItem = null });
                await RunPausableStepAsync(
                    token => _store.ClearAsync(IndexRebuildScope.Images, token), operationToken);
            }

            if (indexDocuments || indexImages)
            {
                await IndexFileVectorsAsync(indexDocuments, indexImages, rebuild, operationToken);
            }

            if (indexDocuments)
            {
                await WaitIfPausedAsync(operationToken);
                UpdateStatus(status => status with { CurrentOperation = "lang.kitopia.updating_app_and_plugin_text_index", CurrentItem = null });
                await IndexGenericTextEntriesAsync(operationToken);
            }
        }
        catch (OperationCanceledException) when (operationCancellation.IsCancellationRequested)
        {
            Logger.Information("Index operation {Scope} was cancelled.", scope);
        }
        catch (Exception exception)
        {
            UpdateStatus(status => status with { LastError = exception.Message });
            throw;
        }
        finally
        {
            if (indexDocuments || indexImages)
            {
                await ReleaseIndexingSessionsAsync();
            }

            FinishOperation(operationCancellation);
            UpdateStatus(status => status with
            {
                IsRebuilding = false,
                IsPaused = false,
                CurrentOperation = null,
                CurrentItem = null,
                ProcessingImages = 0
            });
            _rebuildGate.Release();
            PublishStatus();
        }
    }

    private CancellationTokenSource BeginOperation(CancellationToken cancellationToken)
    {
        var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_operationStateLock)
        {
            _activeOperationCancellation = operationCancellation;
        }

        return operationCancellation;
    }

    private void FinishOperation(CancellationTokenSource operationCancellation)
    {
        TaskCompletionSource<bool>? resumeSignal;
        lock (_operationStateLock)
        {
            if (ReferenceEquals(_activeOperationCancellation, operationCancellation))
            {
                _activeOperationCancellation = null;
            }

            _isPaused = false;
            _activeStepCancellation = null;
            resumeSignal = _isForegroundPaused ? null : _resumeSignal;
            if (!_isForegroundPaused) _resumeSignal = null;
        }

        resumeSignal?.TrySetResult(true);
        operationCancellation.Dispose();
    }

    private bool IsPauseRequested
    {
        get
        {
            lock (_operationStateLock) return _isPaused || _isForegroundPaused;
        }
    }

    private async Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task? resumeTask;
            lock (_operationStateLock)
            {
                if (!_isPaused && !_isForegroundPaused)
                {
                    break;
                }

                resumeTask = _resumeSignal!.Task;
            }

            await resumeTask.WaitAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    internal async Task RunPausableStepAsync(Func<CancellationToken, Task> step, CancellationToken operationToken)
    {
        while (true)
        {
            await WaitIfPausedAsync(operationToken);
            using var stepCancellation = CancellationTokenSource.CreateLinkedTokenSource(operationToken);
            bool pauseRequested;
            lock (_operationStateLock)
            {
                _activeStepCancellation = stepCancellation;
                pauseRequested = _isPaused || _isForegroundPaused;
            }

            try
            {
                if (pauseRequested) stepCancellation.Cancel();
                stepCancellation.Token.ThrowIfCancellationRequested();
                await step(stepCancellation.Token);
                stepCancellation.Token.ThrowIfCancellationRequested();
                return;
            }
            catch (OperationCanceledException) when (!operationToken.IsCancellationRequested
                                                    && stepCancellation.IsCancellationRequested)
            {
                // The current step is retried after the pause; completed writes are idempotent.
            }
            finally
            {
                lock (_operationStateLock)
                {
                    if (ReferenceEquals(_activeStepCancellation, stepCancellation))
                        _activeStepCancellation = null;
                }
            }
        }
    }

    private sealed record ImageIndexWorkItem(
        string Path,
        FileFingerprint Fingerprint,
        FileIndexState? Existing,
        string ContentHash,
        bool NeedsImageVector,
        bool OcrAvailable,
        bool NeedsOcr,
        string? OcrModelId);

    private async Task<ImageIndexWorkItem> PrepareImageIndexAsync(
        string fullPath,
        bool force,
        CancellationToken cancellationToken)
    {
        if (!TryGetEmbeddingService(out var imageEmbeddingService))
        {
            throw new InvalidOperationException("EmbeddingGemma 2 Q4 model files are unavailable.");
        }

        var fingerprint = TryGetFileFingerprint(fullPath)
                          ?? throw new FileNotFoundException("Image file was not found.", fullPath);
        var existing = await _store.GetFileStateAsync(fullPath, IndexFileKind.Image, cancellationToken);
        var imageIsCurrent = await _store.HasImageVectorAsync(
            fullPath, imageEmbeddingService.ModelId, cancellationToken);
        EmbeddingGemmaEmbeddingService? textEmbeddingService = null;
        var ocrAvailable = _ocrService is { IsAvailable: true }
                           && ConfigManger.Config.enableSemanticSearch
                           && TryGetEmbeddingService(out textEmbeddingService);
        var ocrIsCurrent = !ocrAvailable;
        if (ocrAvailable)
        {
            ocrIsCurrent = existing is { OcrCompleted: true }
                           && string.Equals(existing.OcrModelId, textEmbeddingService!.ModelId, StringComparison.Ordinal);
        }

        var metadataMatches = FileStateMatches(existing, fingerprint);
        if (!force && metadataMatches && imageIsCurrent && ocrIsCurrent)
        {
            return new ImageIndexWorkItem(
                fullPath,
                fingerprint,
                existing,
                existing!.ContentHash,
                false,
                ocrAvailable,
                false,
                ocrAvailable ? textEmbeddingService!.ModelId : existing?.OcrModelId);
        }

        var contentHash = metadataMatches
            ? existing!.ContentHash
            : await TryComputeFileContentHashAsync(fullPath, cancellationToken)
              ?? throw new IOException($"Unable to hash image '{fullPath}'.");
        var contentMatches = existing is not null
                             && string.Equals(existing.ContentHash, contentHash, StringComparison.Ordinal);
        return new ImageIndexWorkItem(
            fullPath,
            fingerprint,
            existing,
            contentHash,
            force || !imageIsCurrent || !contentMatches,
            ocrAvailable,
            ocrAvailable && (!ocrIsCurrent || !contentMatches || force),
            ocrAvailable ? textEmbeddingService!.ModelId : existing?.OcrModelId);
    }

    private async Task<HashSet<string>> IndexImageVectorBatchAsync(
        IReadOnlyList<ImageIndexWorkItem> items,
        CancellationToken cancellationToken)
    {
        if (!TryGetEmbeddingService(out var embeddingService))
        {
            throw new InvalidOperationException("EmbeddingGemma 2 Q4 model files are unavailable.");
        }

        var failed = new HashSet<string>(EntryKeyComparer);
        var pending = new List<ImageIndexWorkItem>(items.Count);
        foreach (var item in items)
        {
            if (!item.NeedsImageVector)
            {
                continue;
            }

            try
            {
                var copied = await _store.TryCopyImageVectorForContentHashAsync(
                    item.Path,
                    item.Fingerprint.ToImageFingerprint(),
                    item.ContentHash,
                    embeddingService.ModelId,
                    cancellationToken);
                if (!copied)
                {
                    pending.Add(item);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Logger.Warning(exception, "Failed to reuse image vector for {ImagePath}.", item.Path);
                failed.Add(item.Path);
            }
        }

        if (pending.Count == 0)
        {
            return failed;
        }

        IReadOnlyList<float[]> vectors;
        try
        {
            vectors = await embeddingService.EmbedImagesAsync(
                pending.Select(item => item.Path).ToArray(),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.Warning(
                exception,
                "Failed to embed image vector batch for {ImageCount} images: {ImagePaths}.",
                pending.Count,
                pending.Select(item => item.Path).ToArray());

            foreach (var item in pending)
            {
                try
                {
                    var vector = (await embeddingService.EmbedImagesAsync([item.Path], cancellationToken))[0];
                    await _store.UpsertImageAsync(
                        item.Path,
                        item.Fingerprint.ToImageFingerprint(),
                        embeddingService.ModelId,
                        vector,
                        cancellationToken);
                }
                catch (Exception itemException) when (itemException is not OperationCanceledException)
                {
                    Logger.Warning(itemException, "Failed to index image vector for {ImagePath}.", item.Path);
                    failed.Add(item.Path);
                }
            }

            return failed;
        }

        for (var index = 0; index < pending.Count; index++)
        {
            var item = pending[index];
            try
            {
                await _store.UpsertImageAsync(
                    item.Path,
                    item.Fingerprint.ToImageFingerprint(),
                    embeddingService.ModelId,
                    vectors[index],
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Logger.Warning(exception, "Failed to persist image vector for {ImagePath}.", item.Path);
                failed.Add(item.Path);
            }
        }

        return failed;
    }

    private async Task IndexImageOcrAsync(
        ImageIndexWorkItem item,
        CancellationToken cancellationToken)
    {
        var ocrCompleted = item.Existing?.OcrCompleted ?? false;
        if (item.OcrAvailable && item.NeedsOcr)
        {
            if (!TryGetEmbeddingService(out var textEmbeddingService))
            {
                throw new InvalidOperationException("EmbeddingGemma 2 Q4 model files are unavailable.");
            }

            var copied = await _store.HasCompletedOcrForContentHashAsync(
                item.ContentHash,
                textEmbeddingService.ModelId,
                cancellationToken);
            if (copied)
            {
                copied = await _store.TryCopyOcrTextForContentHashAsync(
                    item.Path,
                    item.ContentHash,
                    textEmbeddingService.ModelId,
                    cancellationToken);
            }

            if (!copied)
            {
                await IndexOcrTextAsync(item.Path, textEmbeddingService, cancellationToken);
            }

            ocrCompleted = true;
        }

        await _store.UpsertFileStateAsync(
            new FileIndexState(
                item.Path,
                IndexFileKind.Image,
                item.Fingerprint.Length,
                item.Fingerprint.LastWriteUtcTicks,
                item.ContentHash,
                ocrCompleted,
                item.OcrAvailable ? item.OcrModelId : item.Existing?.OcrModelId),
            cancellationToken);
    }

    private async Task IndexFileVectorsAsync(
        bool indexDocuments,
        bool indexImages,
        bool force,
        CancellationToken cancellationToken)
    {
        UpdateStatus(status => status with
        {
            CurrentOperation = "lang.kitopia.counting_files_to_index",
            CurrentItem = null,
            TotalFileItems = 0,
            CompletedFileItems = 0
        });
        var fileItems = new List<(string Path, IndexFileKind Kind)>();
        await foreach (var item in EnumerateIndexableFilePathsAsync(indexDocuments, indexImages, cancellationToken))
        {
            await WaitIfPausedAsync(cancellationToken);
            fileItems.Add(item);
        }

        UpdateStatus(status => status with
        {
            CurrentOperation = "lang.kitopia.indexing_files",
            CurrentItem = null,
            TotalFileItems = fileItems.Count,
            CompletedFileItems = 0
        });
        var completedFileItems = 0;

        void MarkCompleted()
        {
            completedFileItems++;
            UpdateStatus(status => status with
            {
                CompletedFileItems = completedFileItems,
                TotalFileItems = Math.Max(status.TotalFileItems, completedFileItems)
            });
        }

        if (indexDocuments)
        {
            EmbeddingGemmaEmbeddingService? documentEmbeddingService = null;
            if (ConfigManger.Config.enableSemanticSearch) TryGetEmbeddingService(out documentEmbeddingService);
            foreach (var (path, _) in fileItems.Where(item => item.Kind == IndexFileKind.Document))
            {
                await WaitIfPausedAsync(cancellationToken);
                UpdateStatus(status => status with
                {
                    CurrentOperation = "lang.kitopia.indexing_documents",
                    CurrentItem = path
                });
                if (documentEmbeddingService is not null)
                {
                    await RunPausableStepAsync(
                        token => IndexDocumentVectorAsync(path, force, documentEmbeddingService, token),
                        cancellationToken);
                }

                MarkCompleted();
            }
        }

        if (!indexImages)
        {
            return;
        }

        var imageWorkItems = new List<ImageIndexWorkItem>();
        foreach (var (path, _) in fileItems.Where(item => item.Kind == IndexFileKind.Image))
        {
            await WaitIfPausedAsync(cancellationToken);
            UpdateStatus(status => status with
            {
                CurrentOperation = "lang.kitopia.preparing_image_index",
                CurrentItem = path
            });
            try
            {
                ImageIndexWorkItem workItem = null!;
                await RunPausableStepAsync(async token =>
                {
                    workItem = await PrepareImageIndexAsync(path, force, token);
                }, cancellationToken);
                if (!workItem.NeedsImageVector
                    && !workItem.NeedsOcr
                    && FileStateMatches(workItem.Existing, workItem.Fingerprint))
                {
                    MarkCompleted();
                    continue;
                }

                imageWorkItems.Add(workItem);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Logger.Warning(exception, "Failed to index image {ImagePath}.", path);
                UpdateStatus(status => status with { FailedImages = status.FailedImages + 1, LastError = exception.Message });
            }
        }

        UpdateStatus(status => status with
        {
            ProcessingImages = imageWorkItems.Count,
            CurrentOperation = "lang.kitopia.indexing_image_embeddings",
            CurrentItem = null
        });
        foreach (var batch in imageWorkItems.Chunk(ImageInferenceBatchSize))
        {
            await WaitIfPausedAsync(cancellationToken);
            var failedVectorItems = new HashSet<string>(EntryKeyComparer);
            try
            {
                await RunPausableStepAsync(async token =>
                {
                    failedVectorItems = await IndexImageVectorBatchAsync(batch, token);
                }, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Logger.Warning(exception, "Failed to run the image vector batch.");
                foreach (var item in batch)
                {
                    if (item.NeedsImageVector)
                    {
                        failedVectorItems.Add(item.Path);
                    }
                }
            }

            foreach (var item in batch)
            {
                await WaitIfPausedAsync(cancellationToken);
                UpdateStatus(status => status with
                {
                    CurrentOperation = item.NeedsOcr ? "lang.kitopia.recognizing_image_text" : "lang.kitopia.updating_image_index",
                    CurrentItem = item.Path
                });
                if (failedVectorItems.Contains(item.Path))
                {
                    UpdateStatus(status => status with
                    {
                        FailedImages = status.FailedImages + 1,
                        LastError = "图片向量处理失败"
                    });
                    MarkCompleted();
                    continue;
                }

                try
                {
                    await RunPausableStepAsync(token => IndexImageOcrAsync(item, token), cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    Logger.Warning(exception, "Failed to index image OCR for {ImagePath}.", item.Path);
                    UpdateStatus(status => status with { FailedImages = status.FailedImages + 1, LastError = exception.Message });
                }

                MarkCompleted();
            }
        }

        UpdateStatus(status => status with { ProcessingImages = 0, CurrentItem = null });
    }

    public void Dispose()
    {
        _embeddingService?.Dispose();
        _rebuildGate.Dispose();
        _pinyinBuildGate.Dispose();
    }

    private async Task ReleaseIndexingSessionsAsync()
    {
        try
        {
            await Task.WhenAll(
                _embeddingService?.ReleaseSessionsAsync() ?? Task.CompletedTask,
                _ocrService?.ReleaseSessionsAsync() ?? Task.CompletedTask);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to release ONNX sessions after indexing.");
        }
    }

    private async Task<IReadOnlyList<RankedVectorMatch>[]> SearchSemanticAsync(string query, int maximumResults, CancellationToken cancellationToken)
    {
        if (!TryGetEmbeddingService(out var embeddingService)) return [];
        try
        {
            var vector = (await embeddingService.EmbedAsync(
                [EmbeddingGemmaEmbeddingService.QueryInstruction + query],
                EmbeddingGemmaEmbeddingService.QueryMaximumTokens,
                cancellationToken))[0];
            var tasks = new[]
            {
                ConfigManger.Config.enableSemanticSearch
                    ? _store.SearchTextAsync(embeddingService.ModelId, vector, maximumResults, cancellationToken)
                    : Task.FromResult<IReadOnlyList<VectorMatch>>([]),
                _store.SearchImagesAsync(embeddingService.ModelId, vector, maximumResults, cancellationToken)
            };
            var matches = await Task.WhenAll(tasks);
            return matches.Select(group => (IReadOnlyList<RankedVectorMatch>)group
                .Select((match, index) => new RankedVectorMatch(match.Key, match.Score, index)).ToArray()).ToArray();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.Warning(exception, "EmbeddingGemma semantic query failed.");
            return [];
        }
    }

    private async Task IndexGenericTextEntriesAsync(CancellationToken cancellationToken)
    {
        if (!ConfigManger.Config.enableSemanticSearch) return;
        if (!TryGetEmbeddingService(out var embeddingService)) return;
        var entries = GetGenericTextEntriesSnapshot();
        foreach (var batch in entries.Chunk(32))
        {
            await RunPausableStepAsync(async token =>
            {
                var pending = new List<string>(batch.Length);
                var contents = new List<string>(batch.Length);
                foreach (var item in batch)
                {
                    if (!await _store.HasTextVectorAsync(item.OnlyKey, embeddingService.ModelId, token))
                    {
                        pending.Add(item.OnlyKey);
                        contents.Add(EmbeddingGemmaEmbeddingService.FormatDocument(CreateTextContent(item), item.DisplayName));
                    }
                }

                if (pending.Count == 0) return;
                var vectors = await embeddingService.EmbedAsync(
                    contents, EmbeddingGemmaEmbeddingService.MetadataMaximumTokens, token);
                for (var index = 0; index < pending.Count; index++)
                    await _store.UpsertTextAsync(pending[index], embeddingService.ModelId, vectors[index], token);
            }, cancellationToken);
        }
    }

    private async Task IndexDocumentVectorAsync(
        string path,
        bool force,
        EmbeddingGemmaEmbeddingService embeddingService,
        CancellationToken cancellationToken)
    {
        try
        {
            var file = TryGetFileFingerprint(path);
            if (file is null) return;
            var state = await _store.GetFileStateAsync(path, IndexFileKind.Document, cancellationToken);
            var vectorExists = await _store.HasTextVectorAsync(path, embeddingService.ModelId, cancellationToken);
            if (!force && FileStateMatches(state, file) && vectorExists)
            {
                return;
            }

            var metadataMatches = FileStateMatches(state, file);
            var contentHash = metadataMatches
                ? state!.ContentHash
                : await TryComputeFileContentHashAsync(path, cancellationToken)
                  ?? throw new IOException($"Unable to hash document '{path}'.");
            var contentMatches = state is not null
                                 && string.Equals(state.ContentHash, contentHash, StringComparison.Ordinal);
            if (!force && contentMatches && vectorExists)
            {
                await _store.UpsertFileStateAsync(
                    new FileIndexState(path, IndexFileKind.Document, file.Length, file.LastWriteUtcTicks, contentHash, false, null),
                    cancellationToken);
                return;
            }

            var copied = await _store.TryCopyDocumentTextForContentHashAsync(
                path,
                contentHash,
                embeddingService.ModelId,
                cancellationToken);
            if (!copied)
            {
                IReadOnlyList<float[]> contentVectors = [];
                if (DocumentTextExtractor.TryCreateSource(path, out var source))
                {
                    try
                    {
                        contentVectors = await EmbedDocumentAsync(
                            source with { ContentHash = contentHash },
                            embeddingService,
                            cancellationToken);
                    }
                    catch (Exception exception)
                        when (DocumentTextExtractor.IsRecoverableDocumentFormatException(exception))
                    {
                        Logger.Debug(
                            "Could not extract document content for {DocumentPath}; indexing file metadata instead. {ExceptionType}: {ExceptionMessage}",
                            path,
                            exception.GetType().Name,
                            exception.Message);
                    }
                }

                if (contentVectors.Count > 0)
                {
                    await _store.UpsertTextChunksAsync(path, embeddingService.ModelId, contentVectors, cancellationToken);
                }
                else
                {
                    var fallback = (await embeddingService.EmbedAsync(
                        [EmbeddingGemmaEmbeddingService.FormatDocument(CreateFileTextContent(path), Path.GetFileName(path))],
                        EmbeddingGemmaEmbeddingService.MetadataMaximumTokens, cancellationToken))[0];
                    await _store.UpsertTextAsync(path, embeddingService.ModelId, fallback, cancellationToken);
                }
            }

            await _store.UpsertFileStateAsync(
                new FileIndexState(path, IndexFileKind.Document, file.Length, file.LastWriteUtcTicks, contentHash, false, null),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.Warning(exception, "Failed to index document content for {DocumentPath}.", path);
        }
    }

    private static async Task<IReadOnlyList<float[]>> EmbedDocumentAsync(
        DocumentContentSource source,
        EmbeddingGemmaEmbeddingService embeddingService,
        CancellationToken cancellationToken)
    {
        var vectors = new List<float[]>();
        var chunks = new List<string>(32);
        var title = Path.GetFileName(source.Path);
        await foreach (var chunk in DocumentTextExtractor.ExtractChunksAsync(
                           source,
                           text => embeddingService.CountDocumentTokens(text, title),
                           cancellationToken))
        {
            chunks.Add(EmbeddingGemmaEmbeddingService.FormatDocument(chunk, title));
            if (chunks.Count < 32) continue;
            vectors.AddRange(await embeddingService.EmbedAsync(
                chunks, EmbeddingGemmaEmbeddingService.IndexingMaximumTokens, cancellationToken));
            chunks.Clear();
        }

        if (chunks.Count > 0)
        {
            vectors.AddRange(await embeddingService.EmbedAsync(
                chunks, EmbeddingGemmaEmbeddingService.IndexingMaximumTokens, cancellationToken));
        }

        return vectors;
    }

    private async Task IndexOcrTextAsync(
        string imagePath,
        EmbeddingGemmaEmbeddingService embeddingService,
        CancellationToken cancellationToken)
    {
        if (_ocrService is null || !_ocrService.IsAvailable)
        {
            return;
        }

        IReadOnlyList<PluginCore.OcrTextRegion> regions;
        regions = await _ocrService.RecognizeFileAsync(imagePath, cancellationToken);

        var textBuilder = new StringBuilder();
        foreach (var region in regions)
        {
            if (string.IsNullOrWhiteSpace(region.Text))
            {
                continue;
            }

            if (textBuilder.Length == MaximumOcrInputCharacters)
            {
                break;
            }

            if (textBuilder.Length > 0)
            {
                textBuilder.Append('\n');
            }

            var remaining = MaximumOcrInputCharacters - textBuilder.Length;
            if (remaining == 0)
            {
                break;
            }

            if (region.Text.Length <= remaining)
            {
                textBuilder.Append(region.Text);
                continue;
            }

            textBuilder.Append(region.Text.AsSpan(0, remaining));
            break;
        }

        if (textBuilder.Length == 0)
        {
            await _store.DeleteOcrTextAsync(imagePath, cancellationToken);
            return;
        }

        var chunker = new DocumentTextExtractor.TextChunker(
            text => embeddingService.CountDocumentTokens(text, null),
            EmbeddingGemmaEmbeddingService.IndexingMaximumTokens);
        var chunks = new List<string>();
        foreach (var memory in textBuilder.GetChunks())
        {
            for (var index = 0; index < memory.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (chunker.Append(memory.Span[index]) is { Length: > 0 } chunk)
                    chunks.Add(EmbeddingGemmaEmbeddingService.FormatDocument(chunk));
            }
        }
        if (chunker.Flush() is { Length: > 0 } finalChunk)
            chunks.Add(EmbeddingGemmaEmbeddingService.FormatDocument(finalChunk));
        if (chunks.Count == 0)
        {
            await _store.DeleteOcrTextAsync(imagePath, cancellationToken);
            return;
        }
        var vectors = await embeddingService.EmbedAsync(
            chunks, EmbeddingGemmaEmbeddingService.IndexingMaximumTokens, cancellationToken);
        await _store.UpsertTextChunksAsync(
            imagePath, embeddingService.ModelId, vectors, cancellationToken, TextContentKind.ImageOcr);
    }

    private bool TryGetEmbeddingService([NotNullWhen(true)] out EmbeddingGemmaEmbeddingService? service)
    {
        service = _embeddingService;
        if (service is not null) return true;
        lock (_entriesLock)
        {
            if (_embeddingService is null && EmbeddingGemmaEmbeddingService.TryCreate(out var created))
            {
                _embeddingService = created;
            }

            service = _embeddingService;
            return service is not null;
        }
    }

    private void PublishStatus()
    {
        Interlocked.Increment(ref _statusPublishVersion);
        if (Interlocked.Exchange(ref _statusPublishQueued, 1) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            while (true)
            {
                var targetVersion = Volatile.Read(ref _statusPublishVersion);
                try
                {
                    var vectors = await _store.GetCountsAsync(CancellationToken.None);
                    var (total, applications, documents, images) = await GetEntryCountsAsync(CancellationToken.None);
                    UpdateStatus(status => new IndexStatusSnapshot(
                        total,
                        applications,
                        documents,
                        images,
                        vectors.TextVectors,
                        vectors.ImageVectors,
                        status.PendingImages,
                        status.ProcessingImages,
                        status.FailedImages,
                        status.IsRebuilding,
                        status.IsPaused,
                        status.TotalFileItems,
                        status.CompletedFileItems,
                        EmbeddingGemmaModelPackage.DisplayName,
                        EmbeddingGemmaModelPackage.DisplayName,
                        status.CurrentOperation,
                        status.CurrentItem,
                        status.LastError,
                        DateTimeOffset.UtcNow));
                }
                catch (Exception exception)
                {
                    Logger.Debug(exception, "Unified index status could not read index.db yet.");
                }

                if (Volatile.Read(ref _statusPublishVersion) == targetVersion)
                {
                    Volatile.Write(ref _statusPublishQueued, 0);
                    if (Volatile.Read(ref _statusPublishVersion) == targetVersion)
                    {
                        return;
                    }

                    if (Interlocked.Exchange(ref _statusPublishQueued, 1) != 0)
                    {
                        return;
                    }
                }
            }
        });
    }

    private void UpdateStatus(Func<IndexStatusSnapshot, IndexStatusSnapshot> update)
    {
        IndexStatusSnapshot current;
        IndexStatusSnapshot next;
        do
        {
            current = GetStatus();
            next = update(current) with { UpdatedAt = DateTimeOffset.UtcNow };
        } while (!ReferenceEquals(Interlocked.CompareExchange(ref _status, next, current), current));

        StatusChanged?.Invoke(this, next);
    }

    private async Task<(int Total, int Applications, int Documents, int Images)> GetEntryCountsAsync(
        CancellationToken cancellationToken)
    {
        var managed = await _store.GetManagedFileCountsAsync(cancellationToken);
        var managedPaths = new HashSet<string>(EntryKeyComparer);
        await foreach (var path in _store.EnumerateManagedFilePathsAsync(cancellationToken))
        {
            managedPaths.Add(path);
        }

        var ignoredPaths = GetIgnoredPathSnapshot();
        lock (_entriesLock)
        {
            var applications = 0;
            var explicitDocuments = 0;
            var explicitImages = 0;
            foreach (var indexed in _entries.Values)
            {
                if (IsIgnoredPath(indexed.Entry.OnlyKey, ignoredPaths))
                {
                    continue;
                }

                if (indexed.Source is IndexSource.Application or IndexSource.Plugin)
                {
                    applications++;
                    continue;
                }

                if (indexed.Source == IndexSource.Image
                    && HasSupportedImageExtension(indexed.Entry.OnlyKey)
                    && !managedPaths.Contains(indexed.Entry.OnlyKey))
                {
                    explicitImages++;
                }
                else if (indexed.Source is IndexSource.Document or IndexSource.Manual
                         && IsSupportedDocument(Path.GetExtension(indexed.Entry.OnlyKey))
                         && !managedPaths.Contains(indexed.Entry.OnlyKey))
                {
                    explicitDocuments++;
                }
            }

            return (
                applications + managed.Total + explicitDocuments + explicitImages,
                applications,
                managed.Documents + explicitDocuments,
                managed.Images + explicitImages);
        }
    }

    private async IAsyncEnumerable<(string Path, IndexFileKind Kind)> EnumerateIndexableFilePathsAsync(
        bool includeDocuments,
        bool includeImages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var ignoredPaths = GetIgnoredPathSnapshot();
        await foreach (var path in _store.EnumerateManagedFilePathsAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsIgnoredPath(path, ignoredPaths))
            {
                continue;
            }

            if (includeDocuments && IsSupportedDocument(Path.GetExtension(path)))
            {
                yield return (path, IndexFileKind.Document);
            }
            else if (includeImages && HasSupportedImageExtension(path))
            {
                yield return (path, IndexFileKind.Image);
            }
        }

        List<IndexedEntry> explicitFileEntries;
        lock (_entriesLock)
        {
            explicitFileEntries = _entries.Values
                .Where(indexed => indexed.Source is IndexSource.Document or IndexSource.Image or IndexSource.Manual)
                .ToList();
        }

        foreach (var indexed in explicitFileEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = indexed.Entry.OnlyKey;
            if (IsIgnoredPath(path, ignoredPaths))
            {
                continue;
            }

            var isManagedFile = await _store.ContainsManagedFilePathAsync(path, cancellationToken);
            if (includeImages
                && indexed.Source == IndexSource.Image
                && HasSupportedImageExtension(path)
                && !isManagedFile)
            {
                yield return (path, IndexFileKind.Image);
            }
            else if (includeDocuments
                     && indexed.Source is IndexSource.Document or IndexSource.Manual
                     && !HasSupportedImageExtension(path)
                     && (!isManagedFile || !IsSupportedDocument(Path.GetExtension(path)))
                     && TryGetFileFingerprint(path) is not null)
            {
                yield return (path, IndexFileKind.Document);
            }
        }
    }

    private IReadOnlyList<SearchEntry> GetGenericTextEntriesSnapshot()
    {
        var ignoredPaths = GetIgnoredPathSnapshot();
        lock (_entriesLock)
        {
            return _entries.Values
                .Where(indexed => !IsIgnoredPath(indexed.Entry.OnlyKey, ignoredPaths)
                                  && indexed.Source != IndexSource.Image
                                  && (indexed.Source is not (IndexSource.Document or IndexSource.Manual or IndexSource.EverythingManaged)
                                      || TryGetFileFingerprint(indexed.Entry.OnlyKey) is null))
                .Select(indexed => indexed.Entry)
                .ToList();
        }
    }

    private static FileFingerprint? TryGetFileFingerprint(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileFingerprint(info.Length, info.LastWriteTimeUtc.Ticks) : null;
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or NotSupportedException
                                         or ArgumentException
                                         or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static async Task<string?> TryComputeFileContentHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                useAsync: true);
            return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or NotSupportedException
                                         or ArgumentException
                                         or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static bool FileStateMatches(FileIndexState? state, FileFingerprint fingerprint) =>
        state is not null
        && state.Length == fingerprint.Length
        && state.LastWriteUtcTicks == fingerprint.LastWriteUtcTicks;

    internal static bool ShouldAutomaticallyIndexFile(string path) =>
        ShouldAutomaticallyIndexFile(
            path,
            enforceAllowedFileExtensions: true,
            ignoredPaths: GetIgnoredPathSnapshot());

    internal static bool ShouldAutomaticallyIndexEverythingFile(string path) =>
        ShouldAutomaticallyIndexFile(
            path,
            enforceAllowedFileExtensions: false,
            ignoredPaths: GetIgnoredPathSnapshot());

    internal static bool ShouldAutomaticallyIndexFile(
        string path,
        bool enforceAllowedFileExtensions,
        IReadOnlyList<string> ignoredPaths)
    {
        try
        {
            if (IsIgnoredPath(path, ignoredPaths))
            {
                return false;
            }

            if (IsAppleDoublePath(path))
            {
                return false;
            }

            var fileName = Path.GetFileName(path);
            if (fileName.StartsWith('$') || fileName.StartsWith("~$", StringComparison.Ordinal))
            {
                return false;
            }

            if (enforceAllowedFileExtensions && !IsAllowedFileExtension(path))
            {
                return false;
            }

            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
            {
                return true;
            }

            foreach (var segment in directory.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment.StartsWith('$') || IsTransientDirectoryName(segment))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsAllowedFileExtension(string path)
    {
        var extension = Path.GetExtension(path);
        IEnumerable<string> configuredExtensions =
            ConfigManger.Configs.TryGetValue("KitopiaConfig", out var config)
            && config is KitopiaConfig kitopiaConfig
                ? kitopiaConfig.allowedFileExtensions ?? KitopiaConfig.DefaultAllowedFileExtensions
                : KitopiaConfig.DefaultAllowedFileExtensions;
        foreach (var configuredExtension in configuredExtensions)
        {
            if (string.IsNullOrWhiteSpace(configuredExtension))
            {
                continue;
            }

            var normalized = configuredExtension.Trim();
            if (normalized == "*")
            {
                return true;
            }

            if (string.IsNullOrEmpty(extension))
            {
                continue;
            }

            if (normalized.StartsWith("*.", StringComparison.Ordinal))
            {
                normalized = normalized[1..];
            }
            else if (normalized[0] != '.')
            {
                normalized = "." + normalized;
            }

            if (extension.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTransientDirectoryName(string name)
    {
        IEnumerable<string> configuredNames =
            ConfigManger.Configs.TryGetValue("KitopiaConfig", out var config)
            && config is KitopiaConfig kitopiaConfig
                ? kitopiaConfig.transientDirectoryNames ?? KitopiaConfig.DefaultTransientDirectoryNames
                : KitopiaConfig.DefaultTransientDirectoryNames;
        return configuredNames.Any(configuredName =>
            !string.IsNullOrWhiteSpace(configuredName)
            && string.Equals(configuredName.Trim(), name, StringComparison.OrdinalIgnoreCase));
    }

    internal static IReadOnlyList<string> GetIgnoredPathSnapshot()
    {
        if (!ConfigManger.Configs.TryGetValue("KitopiaConfig", out var config)
            || config is not KitopiaConfig kitopiaConfig)
        {
            return [];
        }

        var ignoredPaths = new List<string>();
        if (kitopiaConfig.ignoreItems is null)
        {
            return ignoredPaths;
        }

        foreach (var item in kitopiaConfig.ignoreItems)
        {
            if (string.IsNullOrWhiteSpace(item)
                || !TryNormalizePath(item, out var normalizedPath)
                || ignoredPaths.Contains(normalizedPath, EntryKeyComparer))
            {
                continue;
            }

            ignoredPaths.Add(normalizedPath);
        }

        return ignoredPaths;
    }

    internal static bool IsIgnoredPath(string path) =>
        IsIgnoredPath(path, GetIgnoredPathSnapshot());

    internal static bool IsIgnoredPath(string path, IReadOnlyList<string> ignoredPaths)
    {
        if (!TryNormalizePath(path, out var normalizedPath))
        {
            return false;
        }

        return ignoredPaths.Any(ignoredPath => IsSameOrDescendantPath(normalizedPath, ignoredPath));
    }

    internal static bool TryNormalizePath(string path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return !string.IsNullOrWhiteSpace(normalizedPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsSameOrDescendantPath(string path, string root)
    {
        if (EntryKeyComparer.Equals(path, root))
        {
            return true;
        }

        if (root.Length == 0)
        {
            return false;
        }

        if (root[^1] == Path.DirectorySeparatorChar
            || root[^1] == Path.AltDirectorySeparatorChar)
        {
            return path.StartsWith(root, PathComparison);
        }

        return path.StartsWith(root + Path.DirectorySeparatorChar, PathComparison)
               || path.StartsWith(root + Path.AltDirectorySeparatorChar, PathComparison);
    }

    private static bool TryCreateFileEntry(string path, out SearchEntry entry)
    {
        entry = default;
        try
        {
            var extension = Path.GetExtension(path);
            var fileType = extension.ToLowerInvariant() switch
            {
                ".pdf" => PluginCore.FileType.PDF文档,
                ".doc" or ".docx" => PluginCore.FileType.Word文档,
                ".xls" or ".xlsx" => PluginCore.FileType.Excel文档,
                ".ppt" or ".pptx" => PluginCore.FileType.PPT文档,
                ".jpg" or ".jpeg" or ".png" or ".bmp" or ".webp" or ".gif" => PluginCore.FileType.图像,
                _ => PluginCore.FileType.文件
            };
            entry = new SearchEntry
            {
                DisplayName = Path.GetFileNameWithoutExtension(path),
                OnlyKey = path,
                FileType = fileType
            };
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool TryGetManagedFileDisplayName(string path, out string displayName)
    {
        displayName = string.Empty;
        try
        {
            displayName = Path.GetFileNameWithoutExtension(path);
            return !string.IsNullOrWhiteSpace(displayName);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool TryGetManagedFileEntry(string path, out SearchEntry entry)
    {
        if (TryGetFileFingerprint(path) is null)
        {
            entry = default;
            return false;
        }

        return TryCreateFileEntry(path, out entry);
    }

    private static bool ShouldSearchSemantically(string query, int pinyinResultCount) =>
        query.Trim().Length >= MinimumSemanticQueryLength && pinyinResultCount < SemanticFallbackPinyinResultLimit;

    private static string CreateTextContent(SearchEntry entry) => string.Join('\n', entry.DisplayName, entry.FileType, entry.OnlyKey);

    private static string CreateFileTextContent(string path) =>
        string.Join('\n', Path.GetFileNameWithoutExtension(path), Path.GetExtension(path), path);

    private static bool HasSupportedImageExtension(string path) =>
        !IsAppleDoublePath(path)
        && Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".webp" or ".gif";

    private static bool IsAppleDoublePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var segments = path.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0
               && (segments[^1].StartsWith("._", StringComparison.Ordinal)
                   || segments.Any(part => part.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsSupportedDocument(string extension) =>
        extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".doc", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".docx", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".xls", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".ppt", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".pptx", StringComparison.OrdinalIgnoreCase);

    private void DeleteVectorsInBackground(IEnumerable<string> paths)
    {
        _ = Task.Run(async () =>
        {
            await _rebuildGate.WaitAsync();
            try
            {
                foreach (var key in paths)
                {
                    try
                    {
                        lock (_entriesLock)
                        {
                            if (_entries.ContainsKey(key))
                            {
                                continue;
                            }

                            // The store gate makes the manifest check and vector deletion one
                            // operation. Holding the rebuild gate also excludes file indexing
                            // from recreating a vector between those two steps.
                            _store.DeleteIfUnreferenced(key);
                        }
                    }
                    catch (Exception exception)
                    {
                        Logger.Warning(exception, "Failed to delete stale vector for {OnlyKey}.", key);
                    }
                }
            }
            finally
            {
                _rebuildGate.Release();
            }
        });
    }

    private sealed record IndexedEntry(SearchEntry Entry, IndexSource Source);
    private sealed record FileFingerprint(long Length, long LastWriteUtcTicks)
    {
        public string ToImageFingerprint() => $"{Length}:{LastWriteUtcTicks}";
    }
    private sealed record RankedVectorMatch(string Key, double Score, int Rank);
    private sealed record PinyinMatch(
        string Key,
        SearchEntry? Entry,
        double Weight,
        bool[]? CharMatchResults);
}
