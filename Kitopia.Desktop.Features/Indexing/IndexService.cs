using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
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
    private static readonly ILogger Logger = LogManager.Logger.ForContext<IndexService>();
    private static readonly StringComparer EntryKeyComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
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
    private BgeOnnxEmbeddingService? _textEmbeddingService;
    private ChineseClipEmbeddingService? _imageEmbeddingService;
    private readonly IOcrService? _ocrService;
    private IndexStatusSnapshot _status = IndexStatusSnapshot.Empty;

    public event EventHandler<IndexStatusSnapshot>? StatusChanged;

    bool ISearchEntryIndex.TryAdd(SearchEntry entry) => TryAdd(entry);

    public IndexService(IOcrService? ocrService = null)
    {
        _ocrService = ocrService;
    }

    public IndexStatusSnapshot GetStatus() => Volatile.Read(ref _status);

    public bool TryAdd(SearchEntry entry, IndexSource source = IndexSource.Application)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.OnlyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.DisplayName);
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

    public bool TryGetValue(string onlyKey, out SearchEntry entry)
    {
        entry = default;
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
        var incomingByKey = new Dictionary<string, SearchEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
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
        HashSet<string> protectedKeys;
        lock (_entriesLock)
        {
            protectedKeys = _entries.Keys.ToHashSet(EntryKeyComparer);
        }

        var changed = await _store.SynchronizeFileSourceAsync(
            source,
            paths,
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
        lock (_entriesLock)
        {
            return _entries.Select(pair => new KeyValuePair<string, SearchEntry>(pair.Key, pair.Value.Entry)).ToList();
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
        var pinyinResults = SearchPinyinResults(query, PinyinResultLimit, cancellationToken);
        var merged = new Dictionary<string, SearchIndexResult>(EntryKeyComparer);
        for (var index = 0; index < pinyinResults.Count; index++)
        {
            var result = pinyinResults[index];
            merged[result.Source.OnlyKey] = result with { Weight = 1d / (60 + index + 1) };
        }

        // Indexing deliberately runs CLIP, OCR, and BGE one at a time. Do not let an interactive
        // semantic query load another native model session during that memory-sensitive pass.
        if (!ShouldSearchSemantically(query, pinyinResults.Count) || GetStatus().IsRebuilding)
        {
            return merged.Values.OrderByDescending(result => result.Weight).Take(maximumResults).ToList();
        }

        var semanticTasks = new[]
        {
            SearchTextAsync(query, maximumResults, cancellationToken),
            SearchImagesAsync(query, maximumResults, cancellationToken)
        };
        var semanticResults = await Task.WhenAll(semanticTasks);
        foreach (var match in semanticResults.SelectMany(matches => matches))
        {
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
                if (explicitKeys.Contains(path) || !TryGetManagedFileDisplayName(path, out var displayName))
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

    public async Task IndexIncrementalAsync(IndexRebuildScope scope, CancellationToken cancellationToken = default)
    {
        await _rebuildGate.WaitAsync(cancellationToken);
        var indexDocuments = scope is IndexRebuildScope.All or IndexRebuildScope.Documents or IndexRebuildScope.Files;
        var indexImages = scope is IndexRebuildScope.All or IndexRebuildScope.Images or IndexRebuildScope.Files;
        var originalProcessorAffinity = indexDocuments || indexImages
            ? LimitIndexingCpu()
            : null;
        try
        {
            UpdateStatus(status => status with
            {
                IsRebuilding = true,
                CurrentOperation = $"Updating {scope} index", LastError = null
            });

            if (scope is IndexRebuildScope.All or IndexRebuildScope.Pinyin)
            {
                await RebuildPinyinSearcherAsync(cancellationToken);
            }

            if (indexDocuments || indexImages)
            {
                await IndexFileVectorsAsync(indexDocuments, indexImages, force: false, cancellationToken);
            }

            if (indexDocuments)
            {
                await IndexGenericTextEntriesAsync(cancellationToken);
            }
        }
        finally
        {
            if (indexDocuments || indexImages)
            {
                await ReleaseIndexingSessionsAsync();
            }

            RestoreProcessorAffinity(originalProcessorAffinity);
            UpdateStatus(status => status with { IsRebuilding = false, CurrentOperation = null });
            _rebuildGate.Release();
            PublishStatus();
        }
    }

    public async Task RebuildAsync(IndexRebuildScope scope, CancellationToken cancellationToken = default)
    {
        await _rebuildGate.WaitAsync(cancellationToken);
        var indexDocuments = scope is IndexRebuildScope.All or IndexRebuildScope.Documents or IndexRebuildScope.Files;
        var indexImages = scope is IndexRebuildScope.All or IndexRebuildScope.Images or IndexRebuildScope.Files;
        var originalProcessorAffinity = indexDocuments || indexImages
            ? LimitIndexingCpu()
            : null;
        try
        {
            UpdateStatus(status => status with { IsRebuilding = true, CurrentOperation = $"Rebuilding {scope} index", LastError = null });
            if (scope is IndexRebuildScope.All or IndexRebuildScope.Pinyin)
            {
                await RebuildPinyinSearcherAsync(cancellationToken);
            }

            if (indexDocuments)
            {
                await _store.ClearAsync(IndexRebuildScope.Documents, cancellationToken);
            }

            if (indexImages)
            {
                await _store.ClearAsync(IndexRebuildScope.Images, cancellationToken);
            }

            if (indexDocuments || indexImages)
            {
                await IndexFileVectorsAsync(indexDocuments, indexImages, force: true, cancellationToken);
            }

            if (indexDocuments)
            {
                await IndexGenericTextEntriesAsync(cancellationToken);
            }
        }
        finally
        {
            if (indexDocuments || indexImages)
            {
                await ReleaseIndexingSessionsAsync();
            }

            RestoreProcessorAffinity(originalProcessorAffinity);
            UpdateStatus(status => status with { IsRebuilding = false, CurrentOperation = null });
            _rebuildGate.Release();
            PublishStatus();
        }
    }

    private async Task IndexImageWithStatusAsync(
        string fullPath,
        string displayName,
        bool force,
        CancellationToken cancellationToken)
    {
        try
        {
            UpdateStatus(status => status with { ProcessingImages = status.ProcessingImages + 1, CurrentOperation = $"Indexing image: {displayName}", LastError = null });
            if (!TryGetImageEmbeddingService(out var embeddingService))
            {
                throw new InvalidOperationException("Chinese-CLIP RN50 INT8 model files are unavailable.");
            }

            var file = TryGetFileFingerprint(fullPath)
                       ?? throw new FileNotFoundException("Image file was not found.", fullPath);
            var existing = await _store.GetFileStateAsync(fullPath, IndexFileKind.Image, cancellationToken);
            var imageIsCurrent = await _store.HasImageVectorAsync(fullPath, embeddingService.ModelId, cancellationToken);
            BgeOnnxEmbeddingService? textEmbeddingService = null;
            var ocrAvailable = _ocrService is { IsAvailable: true }
                               && TryGetTextEmbeddingService(out textEmbeddingService);
            var ocrIsCurrent = !ocrAvailable
                               || existing is { OcrCompleted: true }
                                  && string.Equals(existing.OcrModelId, textEmbeddingService!.ModelId, StringComparison.Ordinal);
            if (!force && FileStateMatches(existing, file) && imageIsCurrent && ocrIsCurrent)
            {
                return;
            }

            var metadataMatches = FileStateMatches(existing, file);
            var contentHash = metadataMatches
                ? existing!.ContentHash
                : await TryComputeFileContentHashAsync(fullPath, cancellationToken)
                  ?? throw new IOException($"Unable to hash image '{fullPath}'.");
            var contentMatches = existing is not null
                                 && string.Equals(existing.ContentHash, contentHash, StringComparison.Ordinal);
            var fingerprint = file.ToImageFingerprint();
            var needsImageVector = force || !imageIsCurrent || !contentMatches;
            if (needsImageVector)
            {
                var copied = await _store.TryCopyImageVectorForContentHashAsync(
                    fullPath,
                    fingerprint,
                    contentHash,
                    embeddingService.ModelId,
                    cancellationToken);
                if (!copied)
                {
                    // This is a per-file pipeline. BGE from the preceding document or OCR must
                    // be released before CLIP is initialized for this image.
                    await (_textEmbeddingService?.ReleaseSessionAsync() ?? Task.CompletedTask);
                    try
                    {
                        var vector = await embeddingService.EmbedImageAsync(fullPath, cancellationToken);
                        await _store.UpsertImageAsync(
                            fullPath,
                            fingerprint,
                            embeddingService.ModelId,
                            vector,
                            cancellationToken);
                    }
                    finally
                    {
                        await embeddingService.ReleaseImageSessionAsync();
                    }
                }
            }

            // A semantic image search may have initialized CLIP before indexing started. Do
            // not carry that image session into the OCR/BGE part of this same file.
            await embeddingService.ReleaseImageSessionAsync();

            var ocrCompleted = !ocrAvailable
                ? existing?.OcrCompleted ?? false
                : ocrIsCurrent && contentMatches;
            if (ocrAvailable && (!ocrIsCurrent || !contentMatches || force))
            {
                var copied = await _store.HasCompletedOcrForContentHashAsync(
                    contentHash,
                    textEmbeddingService!.ModelId,
                    cancellationToken);
                if (copied)
                {
                    var textCopied = await _store.TryCopyOcrTextForContentHashAsync(
                        fullPath,
                        contentHash,
                        textEmbeddingService.ModelId,
                        cancellationToken);
                    if (!textCopied)
                    {
                        await _store.DeleteOcrTextAsync(fullPath, cancellationToken);
                    }
                }
                else
                {
                    await IndexOcrTextAsync(fullPath, textEmbeddingService!, cancellationToken);
                }

                ocrCompleted = true;
            }

            await _store.UpsertFileStateAsync(
                new FileIndexState(
                    fullPath,
                    IndexFileKind.Image,
                    file.Length,
                    file.LastWriteUtcTicks,
                    contentHash,
                    ocrCompleted,
                    ocrAvailable ? textEmbeddingService!.ModelId : existing?.OcrModelId),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.Warning(exception, "Failed to index image {ImagePath}.", fullPath);
            UpdateStatus(status => status with { FailedImages = status.FailedImages + 1, LastError = exception.Message });
        }
        finally
        {
            try
            {
                await (_imageEmbeddingService?.ReleaseImageSessionAsync() ?? Task.CompletedTask);
            }
            catch (Exception exception)
            {
                Logger.Warning(exception, "Failed to release the CLIP image session for {ImagePath}.", fullPath);
            }

            UpdateStatus(status => status with
            {
                ProcessingImages = Math.Max(0, status.ProcessingImages - 1),
                CurrentOperation = null
            });
            PublishStatus();
        }
    }

    private async Task IndexFileVectorsAsync(
        bool indexDocuments,
        bool indexImages,
        bool force,
        CancellationToken cancellationToken)
    {
        BgeOnnxEmbeddingService? documentEmbeddingService = null;
        var documentEmbeddingChecked = false;

        await foreach (var (path, kind) in EnumerateIndexableFilePathsAsync(
                           indexDocuments,
                           indexImages,
                           cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (kind == IndexFileKind.Document)
            {
                if (!documentEmbeddingChecked)
                {
                    documentEmbeddingChecked = true;
                    TryGetTextEmbeddingService(out documentEmbeddingService);
                }

                if (documentEmbeddingService is not null)
                {
                    await IndexDocumentVectorAsync(path, force, documentEmbeddingService, cancellationToken);
                }

                continue;
            }

            // Consume the manifest one file at a time. Image vectors, OCR recognition, and the
            // OCR/BGE vector are completed before the next path is requested from SQLite.
            await IndexImageWithStatusAsync(
                path,
                Path.GetFileNameWithoutExtension(path),
                force,
                cancellationToken);
        }
    }

    public void Dispose()
    {
        _textEmbeddingService?.Dispose();
        _imageEmbeddingService?.Dispose();
        _rebuildGate.Dispose();
        _pinyinBuildGate.Dispose();
    }

    private async Task ReleaseIndexingSessionsAsync()
    {
        try
        {
            var textEmbeddingService = _textEmbeddingService;
            var imageEmbeddingService = _imageEmbeddingService;
            await Task.WhenAll(
                textEmbeddingService?.ReleaseSessionAsync() ?? Task.CompletedTask,
                imageEmbeddingService?.ReleaseSessionsAsync() ?? Task.CompletedTask,
                _ocrService?.ReleaseSessionsAsync() ?? Task.CompletedTask);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to release ONNX sessions after indexing.");
        }
    }

    private static IntPtr? LimitIndexingCpu()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var usagePercent = Math.Clamp(ConfigManger.Config.indexingMaximumCpuUsagePercent, 1, 100);
        if (usagePercent == 100)
        {
            return null;
        }

        try
        {
            using var process = Process.GetCurrentProcess();
            var originalAffinity = process.ProcessorAffinity;
            var originalMask = unchecked((nuint)originalAffinity.ToInt64());
            var bitCount = IntPtr.Size * 8;
            var availableProcessorCount = 0;
            for (var bit = 0; bit < bitCount; bit++)
            {
                if ((originalMask & ((nuint)1 << bit)) != 0)
                {
                    availableProcessorCount++;
                }
            }

            var maximumProcessors = Math.Max(1, availableProcessorCount * usagePercent / 100);
            if (maximumProcessors >= availableProcessorCount)
            {
                return null;
            }

            var limitedMask = (nuint)0;
            for (var bit = 0; bit < bitCount && maximumProcessors > 0; bit++)
            {
                var processor = (nuint)1 << bit;
                if ((originalMask & processor) == 0)
                {
                    continue;
                }

                limitedMask |= processor;
                maximumProcessors--;
            }

            process.ProcessorAffinity = IntPtr.Size == 8
                ? new IntPtr(unchecked((long)limitedMask))
                : new IntPtr(unchecked((int)limitedMask));
            return originalAffinity;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to limit indexing CPU usage.");
            return null;
        }
    }

    private static void RestoreProcessorAffinity(IntPtr? originalAffinity)
    {
        if (originalAffinity is not { } affinity)
        {
            return;
        }

        try
        {
            using var process = Process.GetCurrentProcess();
            process.ProcessorAffinity = affinity;
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to restore processor affinity after indexing.");
        }
    }

    private async Task<IReadOnlyList<RankedVectorMatch>> SearchTextAsync(string query, int maximumResults, CancellationToken cancellationToken)
    {
        if (!TryGetTextEmbeddingService(out var embeddingService)) return [];
        try
        {
            var vector = (await embeddingService.EmbedAsync(
                [BgeOnnxEmbeddingService.QueryInstruction + query],
                BgeOnnxEmbeddingService.MetadataMaximumTokens,
                cancellationToken))[0];
            var matches = await _store.SearchTextAsync(embeddingService.ModelId, vector, maximumResults, cancellationToken);
            return matches.Select((match, index) => new RankedVectorMatch(match.Key, match.Score, index)).ToArray();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.Warning(exception, "Text semantic query failed.");
            return [];
        }
    }

    private async Task<IReadOnlyList<RankedVectorMatch>> SearchImagesAsync(string query, int maximumResults, CancellationToken cancellationToken)
    {
        if (!TryGetImageEmbeddingService(out var embeddingService)) return [];
        try
        {
            var vector = await embeddingService.EmbedTextAsync(query, cancellationToken);
            var matches = await _store.SearchImagesAsync(embeddingService.ModelId, vector, maximumResults, cancellationToken);
            return matches.Select((match, index) => new RankedVectorMatch(match.Key, match.Score, index)).ToArray();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.Warning(exception, "Image semantic query failed.");
            return [];
        }
    }

    private async Task IndexGenericTextEntriesAsync(CancellationToken cancellationToken)
    {
        if (!TryGetTextEmbeddingService(out var embeddingService)) return;
        var entries = GetGenericTextEntriesSnapshot();
        foreach (var batch in entries.Chunk(32))
        {
            var pending = new List<string>(batch.Length);
            var contents = new List<string>(batch.Length);
            foreach (var item in batch)
            {
                if (!await _store.HasTextVectorAsync(item.OnlyKey, embeddingService.ModelId, cancellationToken))
                {
                    pending.Add(item.OnlyKey);
                    contents.Add(CreateTextContent(item));
                }
            }

            if (pending.Count == 0) continue;
            var vectors = await embeddingService.EmbedAsync(
                contents,
                BgeOnnxEmbeddingService.MetadataMaximumTokens,
                cancellationToken);
            for (var index = 0; index < pending.Count; index++)
            {
                await _store.UpsertTextAsync(pending[index], embeddingService.ModelId, vectors[index], cancellationToken);
            }
        }
    }

    private async Task IndexDocumentVectorAsync(
        string path,
        bool force,
        BgeOnnxEmbeddingService embeddingService,
        CancellationToken cancellationToken)
    {
        UpdateStatus(status => status with { CurrentOperation = $"Indexing document: {Path.GetFileName(path)}" });
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
                var contentVector = DocumentTextExtractor.TryCreateSource(path, out var source)
                    ? await EmbedDocumentAsync(
                        source with { ContentHash = contentHash },
                        embeddingService,
                        cancellationToken)
                    : null;
                if (contentVector is not null)
                {
                    await _store.UpsertDocumentTextAsync(path, embeddingService.ModelId, contentVector, cancellationToken);
                }
                else
                {
                    var fallback = (await embeddingService.EmbedAsync(
                        [CreateFileTextContent(path)], BgeOnnxEmbeddingService.MetadataMaximumTokens, cancellationToken))[0];
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

    private static async Task<float[]?> EmbedDocumentAsync(
        DocumentContentSource source,
        BgeOnnxEmbeddingService embeddingService,
        CancellationToken cancellationToken)
    {
        var total = new double[BgeOnnxEmbeddingService.VectorDimensions];
        var vectorCount = 0;
        var chunks = new List<string>(32);
        await foreach (var chunk in DocumentTextExtractor.ExtractChunksAsync(
                           source,
                           embeddingService.CountTokens,
                           cancellationToken))
        {
            chunks.Add(chunk);
            if (chunks.Count < 32) continue;
            vectorCount += await AddChunkVectorsAsync(chunks, total, embeddingService, cancellationToken);
            chunks.Clear();
        }

        if (chunks.Count > 0)
        {
            vectorCount += await AddChunkVectorsAsync(chunks, total, embeddingService, cancellationToken);
        }

        if (vectorCount == 0) return null;
        var vector = new float[total.Length];
        var squaredLength = 0d;
        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] = (float)(total[index] / vectorCount);
            squaredLength += vector[index] * vector[index];
        }

        var length = Math.Sqrt(squaredLength);
        if (length <= 0) return vector;
        for (var index = 0; index < vector.Length; index++) vector[index] = (float)(vector[index] / length);
        return vector;
    }

    private static async Task<int> AddChunkVectorsAsync(
        IReadOnlyList<string> chunks,
        double[] total,
        BgeOnnxEmbeddingService embeddingService,
        CancellationToken cancellationToken)
    {
        var vectors = await embeddingService.EmbedAsync(
            chunks,
            BgeOnnxEmbeddingService.DocumentMaximumTokens,
            cancellationToken);
        foreach (var vector in vectors)
        {
            for (var index = 0; index < vector.Length; index++) total[index] += vector[index];
        }

        return vectors.Count;
    }

    private async Task IndexOcrTextAsync(
        string imagePath,
        BgeOnnxEmbeddingService embeddingService,
        CancellationToken cancellationToken)
    {
        if (_ocrService is null || !_ocrService.IsAvailable)
        {
            return;
        }

        IReadOnlyList<PluginCore.OcrTextRegion> regions;
        try
        {
            // OCR and BGE both own dynamic ONNX allocations. Release BGE before recognizing,
            // then release OCR before embedding the recognized text.
            await embeddingService.ReleaseSessionAsync();
            regions = await _ocrService.RecognizeFileAsync(imagePath, cancellationToken);
        }
        finally
        {
            await _ocrService.ReleaseSessionsAsync();
        }

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

        var text = textBuilder.ToString();
        try
        {
            var vector = (await embeddingService.EmbedAsync(
                [text],
                BgeOnnxEmbeddingService.MetadataMaximumTokens,
                cancellationToken))[0];
            await _store.UpsertOcrTextAsync(imagePath, embeddingService.ModelId, vector, cancellationToken);
        }
        finally
        {
            await embeddingService.ReleaseSessionAsync();
        }
    }

    private bool TryGetTextEmbeddingService([NotNullWhen(true)] out BgeOnnxEmbeddingService? service)
    {
        service = _textEmbeddingService;
        if (service is not null) return true;
        lock (_entriesLock)
        {
            if (_textEmbeddingService is null && BgeOnnxEmbeddingService.TryCreate(out var created))
            {
                _textEmbeddingService = created;
            }

            service = _textEmbeddingService;
            return service is not null;
        }
    }

    private bool TryGetImageEmbeddingService([NotNullWhen(true)] out ChineseClipEmbeddingService? service)
    {
        service = _imageEmbeddingService;
        if (service is not null) return true;
        lock (_entriesLock)
        {
            if (_imageEmbeddingService is null && ChineseClipEmbeddingService.TryCreate(out var created))
            {
                _imageEmbeddingService = created;
            }

            service = _imageEmbeddingService;
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
                    UpdateStatus(_ => new IndexStatusSnapshot(
                        total,
                        applications,
                        documents,
                        images,
                        vectors.TextVectors,
                        vectors.ImageVectors,
                        0,
                        GetStatus().ProcessingImages,
                        GetStatus().FailedImages,
                        GetStatus().IsRebuilding,
                        "BGE small zh INT8",
                        "Chinese-CLIP RN50 INT8",
                        GetStatus().CurrentOperation,
                        GetStatus().LastError,
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
        var next = update(GetStatus()) with { UpdatedAt = DateTimeOffset.UtcNow };
        Volatile.Write(ref _status, next);
        StatusChanged?.Invoke(this, next);
    }

    private async Task<(int Total, int Applications, int Documents, int Images)> GetEntryCountsAsync(
        CancellationToken cancellationToken)
    {
        var managed = await _store.GetManagedFileCountsAsync(cancellationToken);
        lock (_entriesLock)
        {
            var applications = 0;
            var explicitDocuments = 0;
            var explicitImages = 0;
            foreach (var indexed in _entries.Values)
            {
                if (indexed.Source is IndexSource.Application or IndexSource.Plugin)
                {
                    applications++;
                    continue;
                }

                if (indexed.Source == IndexSource.Image && HasSupportedImageExtension(indexed.Entry.OnlyKey))
                {
                    explicitImages++;
                }
                else if (indexed.Source is IndexSource.Document or IndexSource.Manual
                         && IsSupportedDocument(Path.GetExtension(indexed.Entry.OnlyKey)))
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
        await foreach (var path in _store.EnumerateManagedFilePathsAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
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
        lock (_entriesLock)
        {
            return _entries.Values
                .Where(indexed => indexed.Source != IndexSource.Image
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

    internal static bool ShouldAutomaticallyIndexFile(string path)
    {
        try
        {
            var fileName = Path.GetFileName(path);
            if (fileName.StartsWith('$') || fileName.StartsWith("~$", StringComparison.Ordinal))
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

    private static bool IsTransientDirectoryName(string name) =>
        name.Equals("temp", StringComparison.OrdinalIgnoreCase)
        || name.Equals("tmp", StringComparison.OrdinalIgnoreCase)
        || name.Equals("temporary", StringComparison.OrdinalIgnoreCase)
        || name.Equals("cache", StringComparison.OrdinalIgnoreCase)
        || name.Equals("caches", StringComparison.OrdinalIgnoreCase)
        || name.Equals("inetcache", StringComparison.OrdinalIgnoreCase)
        || name.Equals("temporary internet files", StringComparison.OrdinalIgnoreCase);

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
                ".jpg" or ".jpeg" or ".png" or ".bmp" or ".webp" => PluginCore.FileType.图像,
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
        Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".webp";

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
                    }

                    if (await _store.ContainsManagedFilePathAsync(key, CancellationToken.None))
                    {
                        continue;
                    }

                    await _store.DeleteAsync(key, CancellationToken.None);
                }
                catch (Exception exception)
                {
                    Logger.Warning(exception, "Failed to delete stale vector for {OnlyKey}.", key);
                }
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
