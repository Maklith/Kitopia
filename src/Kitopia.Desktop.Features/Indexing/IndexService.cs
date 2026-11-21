using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Kitopia.Desktop.Features.Search;
using Kitopia.Desktop.Features.Search.Semantic;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services;
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
    private const int ParallelManagedSearchThreshold = 1024;
    private const int ImageInferenceBatchSize = 8;
    private const int CjkRangeStart = '\u4E00';
    private const int CjkRangeLength = '\u9FA5' - '\u4E00' + 1;
    private const uint AsciiLettersMask = 0x03FFFFFFu;
    private const uint HasDigitFlag = 1u << 30;
    private const uint ComplexCharsFlag = 1u << 31;
    private static readonly ILogger Logger = LogManager.Logger.ForContext<IndexService>();
    private static readonly StringComparer EntryKeyComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    // Windows file systems are case-insensitive. Keeping file keys case-sensitive causes
    // Everything to produce a second entry when it changes the casing of a returned path.
    private readonly Dictionary<string, IndexedEntry> _entries = new(EntryKeyComparer);
    private readonly object _entriesLock = new();
    private long _entriesVersion;
    private long _explicitKeysVersion = -1;
    private HashSet<string>? _explicitKeysSnapshot;
    private readonly IndexVectorStore _store = new();
    private static readonly Lazy<PinyinProcessor> ManagedPinyinProcessor = new(() => new PinyinProcessor());
    private static readonly CjkPinyinData?[] CjkPinyinTable = new CjkPinyinData?[CjkRangeLength];
    private static readonly CjkPinyinData EmptyCjkPinyinData = new([], 0, 0);
    private IndexVectorStore? _managedFilesStore;
    private long _managedFilesVersion = -1;
    private CandidateSnapshot? _managedFilesSnapshot;
    private Task<CandidateSnapshot>? _managedFilesLoadTask;
    private IndexVectorStore? _managedFilesLoadStore;
    private long _managedFilesLoadVersion = -1;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private int _disposed;
    private readonly SemaphoreSlim _rebuildGate = new(1, 1);
    private readonly SemaphoreSlim _pinyinBuildGate = new(1, 1);
    private CandidateSnapshot? _explicitPinyinSnapshot;
    private int _pinyinRebuildVersion;
    private int _pinyinRebuildQueued;
    private int _statusPublishQueued;
    private int _statusPublishVersion;
    private Lazy<EmbeddingGemmaEmbeddingService?>? _embeddingService;
    private IndexStatusSnapshot _status = IndexStatusSnapshot.Empty;
    private readonly object _operationStateLock = new();
    private CancellationTokenSource? _activeOperationCancellation;
    private CancellationTokenSource? _activeStepCancellation;
    private TaskCompletionSource<bool>? _resumeSignal;
    private bool _isPaused;
    private bool _isForegroundPaused;

    public static PinyinProcessor SharedPinyinProcessor => ManagedPinyinProcessor.Value;

    public event EventHandler<IndexStatusSnapshot>? StatusChanged;

    bool ISearchEntryIndex.TryAdd(SearchEntry entry) => TryAdd(entry);

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
            _entriesVersion++;
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
                _entriesVersion++;
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
                if (_entries.Remove(key))
                {
                    _entriesVersion++;
                }
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

            if (changed)
            {
                _entriesVersion++;
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
            PublishStatus();
        }

        if (changed || !HasCurrentManagedFilesSnapshot())
        {
            try
            {
                await GetManagedFilesSnapshotAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Logger.Debug(exception, "Could not warm managed-file pinyin snapshot after sync.");
            }
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
                // Managed files are loaded separately from index.db so frequent application/window
                // refreshes do not rescan or duplicate managed file metadata.
                List<SearchEntry> entries;
                lock (_entriesLock)
                {
                    entries = new List<SearchEntry>(_entries.Count);
                    foreach (var indexed in _entries.Values)
                    {
                        entries.Add(indexed.Entry);
                    }
                }

                var candidates = new SearchCandidate[entries.Count];
                var paths = new string[entries.Count];
                var explicitEntries = new SearchEntry[entries.Count];
                for (var index = 0; index < entries.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = entries[index];
                    candidates[index] = BuildSearchCandidate(entry.DisplayName);
                    paths[index] = entry.OnlyKey;
                    explicitEntries[index] = entry;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (Volatile.Read(ref _pinyinRebuildVersion) == version)
                {
                    Volatile.Write(
                        ref _explicitPinyinSnapshot,
                        new CandidateSnapshot(candidates, paths, explicitEntries));
                }
            }, cancellationToken);
        }
        finally
        {
            _pinyinBuildGate.Release();
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
        CancellationToken cancellationToken,
        IReadOnlyList<SearchIndexResult>? pinyinResults = null)
    {
        if (string.IsNullOrWhiteSpace(query) || maximumResults <= 0) return [];
        cancellationToken.ThrowIfCancellationRequested();
        var ignoredPaths = GetIgnoredPathSnapshot();
        pinyinResults ??= SearchPinyinResults(query, PinyinResultLimit, cancellationToken);
        var merged = new Dictionary<string, SearchIndexResult>(EntryKeyComparer);
        for (var index = 0; index < pinyinResults.Count; index++)
        {
            var result = pinyinResults[index];
            merged[result.Source.OnlyKey] = result with { Weight = 1d / (60 + index + 1) };
        }

        if (!SearchIndex.ShouldSearchSemantically(query, pinyinResults.Count))
        {
            return merged.Values.OrderByDescending(result => result.Weight).Take(maximumResults).ToList();
        }

        var semanticResults = await SearchSemanticAsync(query, maximumResults, cancellationToken);
        foreach (var group in semanticResults.SelectMany(matches => matches)
                     .GroupBy(match => match.Key, EntryKeyComparer))
        {
            if (IsIgnoredPath(group.Key, ignoredPaths))
            {
                continue;
            }

            if (!TryGetValue(group.Key, out var entry)) continue;
            // Use the strongest semantic channel per file before adding lexical matches.
            var score = group.Max(match => Math.Max(0d, match.Score) / (60 + match.Rank + 1));
            if (merged.TryGetValue(group.Key, out var existing))
            {
                merged[group.Key] = existing with { Weight = existing.Weight + score };
            }
            else
            {
                merged[group.Key] = new SearchIndexResult(entry, score, null);
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
        var keywords = query.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (keywords.Length == 0)
        {
            return [];
        }

        var queryContext = CreateQueryContext(keywords);
        var merged = new PriorityQueue<MergedCandidateMatch, MergedCandidateMatch>(Math.Min(candidateLimit, 64));
        var explicitKeys = GetExplicitKeysSnapshot();

        var explicitSnapshot = Volatile.Read(ref _explicitPinyinSnapshot);
        if (explicitSnapshot is { Count: > 0 })
        {
            SearchCandidates(
                explicitSnapshot,
                queryContext,
                explicitKeys,
                ignoredPaths,
                isExplicit: true,
                merged,
                candidateLimit,
                double.NegativeInfinity,
                cancellationToken);
        }

        var initialMinWeight = merged.Count >= candidateLimit && merged.TryPeek(out var worstExplicit, out _)
            ? worstExplicit.Weight
            : double.NegativeInfinity;

        try
        {
            var managedSnapshot = GetManagedFilesSnapshot(cancellationToken);
            if (managedSnapshot.Count > 0)
            {
                SearchCandidates(
                    managedSnapshot,
                    queryContext,
                    explicitKeys,
                    ignoredPaths,
                    isExplicit: false,
                    merged,
                    candidateLimit,
                    initialMinWeight,
                    cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A temporary database/read failure must not make the interactive search fail. The
            // application/plugin snapshot collected above is still a valid result set.
            Logger.Debug(exception, "Managed-file pinyin search could not read index.db.");
        }

        if (merged.Count == 0)
        {
            return [];
        }

        var ranked = new MergedCandidateMatch[merged.Count];
        var rankedCount = 0;
        foreach (var (element, _) in merged.UnorderedItems)
        {
            ranked[rankedCount++] = element;
        }

        Array.Sort(ranked, static (left, right) => right.CompareTo(left));

        var results = new List<SearchIndexResult>(Math.Min(maximumResults, rankedCount));
        var seenKeys = new HashSet<string>(Math.Min(maximumResults, rankedCount), EntryKeyComparer);
        for (var i = 0; i < rankedCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ref readonly var match = ref ranked[i];
            if (!seenKeys.Add(match.Path))
            {
                continue;
            }

            SearchEntry entry;
            if (match.Snapshot.ExplicitEntries is { } explicitEntries)
            {
                entry = explicitEntries[match.CandidateIndex];
            }
            else if (!TryGetManagedFileEntry(match.Path, out entry))
            {
                continue;
            }

            var charMatchResults = CreateCharMatches(
                entry.DisplayName,
                in queryContext,
                match.Snapshot.Candidates[match.CandidateIndex].HasComplexChars);
            results.Add(new SearchIndexResult(
                entry,
                1d / (60 + results.Count + 1),
                charMatchResults));
            if (results.Count == maximumResults)
            {
                break;
            }
        }

        return results;
    }

    private HashSet<string> GetExplicitKeysSnapshot()
    {
        lock (_entriesLock)
        {
            if (_explicitKeysSnapshot is not null && _explicitKeysVersion == _entriesVersion)
            {
                return _explicitKeysSnapshot;
            }

            var keys = new HashSet<string>(_entries.Keys, EntryKeyComparer);
            _explicitKeysSnapshot = keys;
            _explicitKeysVersion = _entriesVersion;
            return keys;
        }
    }

    private static void SearchCandidates(
        CandidateSnapshot snapshot,
        QueryContext query,
        IReadOnlySet<string> explicitKeys,
        IReadOnlyList<string> ignoredPaths,
        bool isExplicit,
        PriorityQueue<MergedCandidateMatch, MergedCandidateMatch> merged,
        int candidateLimit,
        double initialMinWeight,
        CancellationToken cancellationToken)
    {
        if (snapshot.Count <= ParallelManagedSearchThreshold)
        {
            var localMatches = new PriorityQueue<SearchCandidateMatch, SearchCandidateMatch>(
                Math.Min(candidateLimit, 64));
            ScanCandidateRange(
                snapshot,
                0,
                snapshot.Count,
                in query,
                explicitKeys,
                ignoredPaths,
                isExplicit,
                candidateLimit,
                initialMinWeight,
                localMatches,
                cancellationToken);
            MergeCandidateMatches(snapshot, localMatches, merged, candidateLimit);
            return;
        }

        var partitions = new List<PriorityQueue<SearchCandidateMatch, SearchCandidateMatch>>();
        Parallel.ForEach(
            Partitioner.Create(0, snapshot.Count),
            new ParallelOptions { CancellationToken = cancellationToken },
            () => new PriorityQueue<SearchCandidateMatch, SearchCandidateMatch>(Math.Min(candidateLimit, 64)),
            (range, _, localMatches) =>
            {
                ScanCandidateRange(
                    snapshot,
                    range.Item1,
                    range.Item2,
                    in query,
                    explicitKeys,
                    ignoredPaths,
                    isExplicit,
                    candidateLimit,
                    initialMinWeight,
                    localMatches,
                    cancellationToken);
                return localMatches;
            },
            localMatches =>
            {
                if (localMatches.Count == 0)
                {
                    return;
                }

                lock (partitions)
                {
                    partitions.Add(localMatches);
                }
            });

        foreach (var partition in partitions)
        {
            MergeCandidateMatches(snapshot, partition, merged, candidateLimit);
        }
    }

    private static void ScanCandidateRange(
        CandidateSnapshot snapshot,
        int startInclusive,
        int endExclusive,
        in QueryContext query,
        IReadOnlySet<string> explicitKeys,
        IReadOnlyList<string> ignoredPaths,
        bool isExplicit,
        int candidateLimit,
        double initialMinWeight,
        PriorityQueue<SearchCandidateMatch, SearchCandidateMatch> localMatches,
        CancellationToken cancellationToken)
    {
        var candidates = snapshot.Candidates;
        var paths = snapshot.Paths;
        for (var index = startInclusive; index < endExclusive; index++)
        {
            if (((index - startInclusive) & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            ref readonly var candidate = ref candidates[index];
            if (!MatchesPrefilter(in candidate, in query))
            {
                continue;
            }

            var localMinWeight = localMatches.Count >= candidateLimit && localMatches.TryPeek(out var worstMatch, out _)
                ? worstMatch.Weight
                : double.NegativeInfinity;
            var minWeight = Math.Max(initialMinWeight, localMinWeight);
            if (!TryMatchPinyin(
                    candidate.DisplayName,
                    in query,
                    candidate.HasComplexChars,
                    null,
                    minWeight,
                    out var weight))
            {
                continue;
            }

            var path = paths[index];
            if ((isExplicit ? !explicitKeys.Contains(path) : explicitKeys.Contains(path))
                || IsIgnoredPath(path, ignoredPaths))
            {
                continue;
            }

            var match = new SearchCandidateMatch(index, weight, path);
            if (localMatches.Count < candidateLimit)
            {
                localMatches.Enqueue(match, match);
            }
            else if (localMatches.TryPeek(out var worst, out _) && match.CompareTo(worst) > 0)
            {
                localMatches.EnqueueDequeue(match, match);
            }
        }
    }

    private static void MergeCandidateMatches(
        CandidateSnapshot snapshot,
        PriorityQueue<SearchCandidateMatch, SearchCandidateMatch> partition,
        PriorityQueue<MergedCandidateMatch, MergedCandidateMatch> merged,
        int candidateLimit)
    {
        foreach (var (element, _) in partition.UnorderedItems)
        {
            var match = new MergedCandidateMatch(snapshot, element.CandidateIndex, element.Weight, element.Path);
            if (merged.Count < candidateLimit)
            {
                merged.Enqueue(match, match);
            }
            else if (merged.TryPeek(out var worst, out _) && match.CompareTo(worst) > 0)
            {
                merged.EnqueueDequeue(match, match);
            }
        }
    }

    private bool HasCurrentManagedFilesSnapshot()
    {
        var store = _store;
        var version = store.ManagedFileSourceVersion;
        lock (_entriesLock)
        {
            return _managedFilesSnapshot is not null
                   && ReferenceEquals(_managedFilesStore, store)
                   && _managedFilesVersion == version;
        }
    }

    private bool TryGetOrStartManagedFilesSnapshot(
        out CandidateSnapshot? snapshot,
        out Task<CandidateSnapshot>? loadTask)
    {
        var store = _store;
        var version = store.ManagedFileSourceVersion;
        lock (_entriesLock)
        {
            if (_managedFilesSnapshot is not null
                && ReferenceEquals(_managedFilesStore, store)
                && _managedFilesVersion == version)
            {
                snapshot = _managedFilesSnapshot;
                loadTask = null;
                return true;
            }

            if (Volatile.Read(ref _disposed) != 0)
            {
                snapshot = null;
                loadTask = null;
                return false;
            }

            snapshot = null;
            loadTask = GetOrStartManagedFilesLoadTaskLocked(store, version);
            return false;
        }
    }

    private Task<CandidateSnapshot> GetOrStartManagedFilesLoadTaskLocked(IndexVectorStore store, long version)
    {
        if (_managedFilesLoadTask is not null
            && ReferenceEquals(_managedFilesLoadStore, store)
            && _managedFilesLoadVersion == version)
        {
            return _managedFilesLoadTask;
        }

        _managedFilesLoadStore = store;
        _managedFilesLoadVersion = version;
        var disposeToken = _disposeCancellation.Token;
        _managedFilesLoadTask = Task.Run(
            () => BuildManagedFilesSnapshot(store, version, disposeToken),
            CancellationToken.None);
        return _managedFilesLoadTask;
    }

    private CandidateSnapshot GetManagedFilesSnapshot(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TryGetOrStartManagedFilesSnapshot(out var snapshot, out var loadTask))
        {
            return snapshot!;
        }

        return loadTask is null
            ? CandidateSnapshot.Empty
            : loadTask.WaitAsync(cancellationToken).GetAwaiter().GetResult();
    }

    private async Task GetManagedFilesSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (TryGetOrStartManagedFilesSnapshot(out _, out var loadTask) || loadTask is null)
        {
            return;
        }

        await loadTask.WaitAsync(cancellationToken);
    }

    private CandidateSnapshot BuildManagedFilesSnapshot(
        IndexVectorStore store,
        long version,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paths = store.ReadManagedFilePathsSnapshot(cancellationToken);
            var count = paths.Count;
            if (count == 0)
            {
                lock (_entriesLock)
                {
                    if (ReferenceEquals(_store, store) && store.ManagedFileSourceVersion == version)
                    {
                        _managedFilesStore = store;
                        _managedFilesVersion = version;
                        _managedFilesSnapshot = CandidateSnapshot.Empty;
                    }
                }

                return CandidateSnapshot.Empty;
            }

            var candidates = new SearchCandidate[count];
            var candidatePaths = new string[count];
            var validCount = 0;
            for (var index = 0; index < count; index++)
            {
                if ((index & 255) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var path = paths[index];
                if (TryGetManagedFileDisplayName(path, out var displayName))
                {
                    candidates[validCount] = BuildSearchCandidate(displayName);
                    candidatePaths[validCount] = path;
                    validCount++;
                }
            }

            if (validCount != count)
            {
                Array.Resize(ref candidates, validCount);
                Array.Resize(ref candidatePaths, validCount);
            }

            var snapshot = validCount == 0
                ? CandidateSnapshot.Empty
                : new CandidateSnapshot(candidates, candidatePaths);
            lock (_entriesLock)
            {
                if (ReferenceEquals(_store, store) && store.ManagedFileSourceVersion == version)
                {
                    _managedFilesStore = store;
                    _managedFilesVersion = version;
                    _managedFilesSnapshot = snapshot;
                }
            }

            return snapshot;
        }
        finally
        {
            lock (_entriesLock)
            {
                if (ReferenceEquals(_managedFilesLoadStore, store) && _managedFilesLoadVersion == version)
                {
                    _managedFilesLoadTask = null;
                }
            }
        }
    }

    internal readonly record struct PinyinSearchItem<T>(T Source, SearchCandidate Candidate);

    internal static PinyinSearchItem<T>[] CreatePinyinSearchItems<T>(
        IReadOnlyList<T> items,
        Func<T, string> textSelector)
    {
        var searchItems = new PinyinSearchItem<T>[items.Count];
        var count = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var text = textSelector(item);
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            searchItems[count++] = new PinyinSearchItem<T>(item, BuildSearchCandidate(text));
        }

        if (count != searchItems.Length)
        {
            Array.Resize(ref searchItems, count);
        }

        return searchItems;
    }

    internal static List<T> SearchPinyinItems<T>(
        PinyinSearchItem<T>[] items,
        string query)
    {
        if (items.Length == 0 || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var keywords = query.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (keywords.Length == 0)
        {
            return [];
        }

        var queryContext = CreateQueryContext(keywords);
        var matches = new List<(T Source, double Weight, int Length)>();
        for (var i = 0; i < items.Length; i++)
        {
            var candidate = items[i].Candidate;
            if (!MatchesPrefilter(in candidate, in queryContext)
                || !TryMatchPinyin(
                    candidate.DisplayName,
                    in queryContext,
                    candidate.HasComplexChars,
                    null,
                    double.NegativeInfinity,
                    out var weight))
            {
                continue;
            }

            matches.Add((items[i].Source, weight, candidate.DisplayName.Length));
        }

        matches.Sort(static (left, right) =>
        {
            var weightComparison = right.Weight.CompareTo(left.Weight);
            return weightComparison != 0
                ? weightComparison
                : left.Length.CompareTo(right.Length);
        });

        var results = new List<T>(matches.Count);
        for (var i = 0; i < matches.Count; i++)
        {
            results.Add(matches[i].Source);
        }

        return results;
    }

    private static SearchCandidate BuildSearchCandidate(string displayName)
    {
        uint letterMask = 0;
        uint startLetterMask = 0;
        foreach (var ch in displayName)
        {
            if (ch is >= 'a' and <= 'z')
            {
                var bit = 1u << (ch - 'a');
                letterMask |= bit;
                startLetterMask |= bit;
            }
            else if (ch is >= 'A' and <= 'Z')
            {
                var bit = 1u << (ch - 'A');
                letterMask |= bit;
                startLetterMask |= bit;
            }
            else if (ch is >= '0' and <= '9')
            {
                letterMask |= HasDigitFlag;
            }
            else if (IsChineseChar(ch))
            {
                var pinyin = GetCjkPinyinData(ch);
                letterMask |= ComplexCharsFlag | pinyin.FullMask;
                startLetterMask |= pinyin.FirstMask;
            }
            else if (ch > 127)
            {
                letterMask |= AsciiLettersMask | ComplexCharsFlag;
                startLetterMask |= AsciiLettersMask;
            }
        }

        return new SearchCandidate(displayName, letterMask, startLetterMask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static CjkPinyinData GetCjkPinyinData(char ch)
    {
        var index = (uint)(ch - CjkRangeStart);
        if (index >= (uint)CjkPinyinTable.Length)
        {
            return EmptyCjkPinyinData;
        }

        var cached = Volatile.Read(ref CjkPinyinTable[index]);
        if (cached is not null)
        {
            return cached;
        }

        var loaded = LoadCjkPinyinData(ch);
        Volatile.Write(ref CjkPinyinTable[index], loaded);
        return loaded;
    }

    private static CjkPinyinData LoadCjkPinyinData(char ch)
    {
        var pinyin = ManagedPinyinProcessor.Value.GetPinyin(ch.ToString());
        if (pinyin.Keys is null || pinyin.Keys.Count == 0)
        {
            return EmptyCjkPinyinData;
        }

        uint fullMask = 0;
        uint firstMask = 0;
        List<string>? syllables = null;
        foreach (var entry in pinyin.Keys)
        {
            foreach (var syllable in entry)
            {
                if (string.IsNullOrEmpty(syllable))
                {
                    continue;
                }

                var hasAsciiPinyin = false;
                var first = syllable[0];
                if (first is >= 'a' and <= 'z')
                {
                    firstMask |= 1u << (first - 'a');
                }
                else if (first is >= 'A' and <= 'Z')
                {
                    firstMask |= 1u << (first - 'A');
                }

                foreach (var letter in syllable)
                {
                    if (letter is >= 'a' and <= 'z')
                    {
                        fullMask |= 1u << (letter - 'a');
                        hasAsciiPinyin = true;
                    }
                    else if (letter is >= 'A' and <= 'Z')
                    {
                        fullMask |= 1u << (letter - 'A');
                        hasAsciiPinyin = true;
                    }
                    else if (letter is >= '0' and <= '9')
                    {
                        fullMask |= HasDigitFlag;
                    }
                }

                if (hasAsciiPinyin)
                {
                    syllables ??= new List<string>(entry.Count);
                    syllables.Add(syllable);
                }
            }
        }

        return syllables is null
            ? EmptyCjkPinyinData
            : new CjkPinyinData(syllables.ToArray(), fullMask, firstMask);
    }

    private static QueryContext CreateQueryContext(string[] keywords)
    {
        uint letterMask = 0;
        uint startMask = 0;
        var hasNonLowerAscii = false;
        var count = keywords.Length;
        var remainingChars = new int[count];
        var remainingDeductions = new double[count];

        for (var i = 0; i < count; i++)
        {
            var keyword = keywords[i];
            var first = keyword[0];
            if (first is >= 'a' and <= 'z')
            {
                startMask |= 1u << (first - 'a');
            }

            foreach (var ch in keyword)
            {
                if (ch is >= 'a' and <= 'z')
                {
                    letterMask |= 1u << (ch - 'a');
                }
                else
                {
                    hasNonLowerAscii = true;
                    if (ch is >= '0' and <= '9')
                    {
                        letterMask |= HasDigitFlag;
                    }
                    else if (ch > 127)
                    {
                        letterMask |= ComplexCharsFlag;
                    }
                }
            }
        }

        var suffixChars = 0;
        double suffixDeductions = 0;
        for (var i = count - 1; i >= 0; i--)
        {
            var length = keywords[i].Length;
            suffixChars += length;
            suffixDeductions += length * (double)(count - i);
            remainingChars[i] = suffixChars;
            remainingDeductions[i] = suffixDeductions;
        }

        var isSingleLowerAsciiChar = count == 1
                                     && keywords[0].Length == 1
                                     && keywords[0][0] is >= 'a' and <= 'z';
        return new QueryContext(
            keywords,
            letterMask,
            startMask,
            hasNonLowerAscii,
            isSingleLowerAsciiChar,
            remainingChars,
            remainingDeductions);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool MatchesPrefilter(in SearchCandidate candidate, in QueryContext query)
    {
        if ((candidate.LetterMaskAndFlags & query.LetterMask) != query.LetterMask
            || (candidate.StartLetterMask & query.StartMask) != query.StartMask)
        {
            return false;
        }

        if (!candidate.HasComplexChars)
        {
            return query.IsSingleLowerAsciiChar
                   || MatchesKeywordsInOrderIgnoreCase(candidate.DisplayName, query.Keywords);
        }

        if (!query.HasNonLowerAscii)
        {
            return true;
        }

        var searchIndex = 0;
        foreach (var keyword in query.Keywords)
        {
            foreach (var ch in keyword)
            {
                if (ch is >= 'a' and <= 'z')
                {
                    continue;
                }

                var foundIndex = IndexOfIgnoreCase(candidate.DisplayName, ch, searchIndex);
                if (foundIndex < 0)
                {
                    return false;
                }

                searchIndex = foundIndex + 1;
            }
        }

        return true;
    }

    private static bool[]? CreateCharMatches(string displayName, in QueryContext query, bool hasComplexChars)
    {
        if (displayName.Length == 0 || query.Keywords.Length == 0)
        {
            return null;
        }

        var highlights = new bool[displayName.Length];
        return TryMatchPinyin(displayName, in query, hasComplexChars, highlights, double.NegativeInfinity, out _)
            ? highlights
            : null;
    }

    private static bool TryMatchPinyin(
        string text,
        in QueryContext query,
        bool hasComplexChars,
        bool[]? charMatchResults,
        double minWeight,
        out double weight)
    {
        weight = 0;
        var globalCharIndex = 0;
        var keywords = query.Keywords;

        for (var keywordIndex = 0; keywordIndex < keywords.Length; keywordIndex++)
        {
            var keyword = keywords[keywordIndex];
            var remainingKeywords = keywords.Length - keywordIndex;
            var maxStartExclusive = hasComplexChars
                ? text.Length - remainingKeywords + 1
                : text.Length - query.RemainingAsciiChars[keywordIndex] + 1;
            var remainingDeduction = hasComplexChars
                ? remainingKeywords * (remainingKeywords + 1) * 0.5d
                : query.RemainingAsciiWeightDeductions[keywordIndex];
            var deadJumpStart = int.MaxValue;
            var deadJumpEnd = -1;
            var matched = false;
            for (var startPos = globalCharIndex; startPos < maxStartExclusive; startPos++)
            {
                if (minWeight > double.NegativeInfinity
                    && weight + remainingKeywords * (1000d - startPos) - remainingDeduction < minWeight)
                {
                    break;
                }

                if (IsPinyinMatchSequence(
                        text,
                        startPos,
                        keyword,
                        charMatchResults,
                        ref deadJumpStart,
                        ref deadJumpEnd,
                        out var scanEndPos))
                {
                    weight += 1000 - scanEndPos;
                    globalCharIndex = scanEndPos;
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                if (charMatchResults is not null)
                {
                    Array.Clear(charMatchResults);
                }

                return false;
            }
        }

        return true;
    }

    private static bool IsPinyinMatchSequence(
        string text,
        int startPos,
        string keyword,
        bool[]? charMatchResults,
        ref int deadJumpStart,
        ref int deadJumpEnd,
        out int scanEndPos)
    {
        scanEndPos = startPos;
        var kIdx = 0;
        var sIdx = startPos;
        var gapStart = -1;
        var firstK0Jump = -1;
        var lastK0Pos = -1;

        while (kIdx < keyword.Length && sIdx < text.Length)
        {
            var currentChar = text[sIdx];
            if (IsPinyinSearcherSplitChar(currentChar))
            {
                if (keyword[kIdx] != currentChar)
                {
                    if (gapStart < 0)
                    {
                        gapStart = sIdx;
                    }

                    sIdx++;
                    if (kIdx == 0 && firstK0Jump >= 0)
                    {
                        lastK0Pos = sIdx;
                    }

                    continue;
                }
            }

            if (IsChineseChar(currentChar))
            {
                var bestMatchLen = 0;
                var fullPinyins = GetCjkPinyinData(currentChar).Full;
                if (fullPinyins is { Length: > 0 })
                {
                    for (var i = 0; i < fullPinyins.Length; i++)
                    {
                        var syllable = fullPinyins[i];
                        var len = 0;
                        var maxLen = Math.Min(keyword.Length - kIdx, syllable.Length);
                        while (len < maxLen && keyword[kIdx + len] == syllable[len])
                        {
                            len++;
                        }

                        if (len > bestMatchLen)
                        {
                            bestMatchLen = len;
                        }
                    }
                }

                if (bestMatchLen > 0)
                {
                    MarkMatchedIndex(charMatchResults, ref gapStart, sIdx);
                    kIdx += bestMatchLen;
                    sIdx++;
                    continue;
                }

                if (ToLowerAsciiOrInvariant(currentChar) == keyword[kIdx])
                {
                    MarkMatchedIndex(charMatchResults, ref gapStart, sIdx);
                    kIdx++;
                    sIdx++;
                    continue;
                }

                break;
            }

            if (ToLowerAsciiOrInvariant(currentChar) == keyword[kIdx])
            {
                MarkMatchedIndex(charMatchResults, ref gapStart, sIdx);
                kIdx++;
                sIdx++;
                continue;
            }

            var tokenEnd = FindNonCjkTokenEnd(text, sIdx);
            if (tokenEnd <= sIdx)
            {
                break;
            }

            if (kIdx == 0)
            {
                if (tokenEnd >= deadJumpStart && tokenEnd <= deadJumpEnd)
                {
                    lastK0Pos = deadJumpEnd;
                    break;
                }

                if (firstK0Jump < 0)
                {
                    firstK0Jump = tokenEnd;
                }

                lastK0Pos = tokenEnd;
            }

            gapStart = -1;
            sIdx = tokenEnd;
        }

        if (kIdx >= keyword.Length)
        {
            scanEndPos = sIdx;
            return true;
        }

        if (firstK0Jump >= 0)
        {
            deadJumpStart = firstK0Jump;
            deadJumpEnd = lastK0Pos;
        }

        if (charMatchResults is not null && sIdx > startPos)
        {
            Array.Clear(charMatchResults, startPos, sIdx - startPos);
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void MarkMatchedIndex(bool[]? charMatchResults, ref int gapStart, int index)
    {
        if (charMatchResults is not null)
        {
            if (gapStart >= 0)
            {
                for (var gapIndex = gapStart; gapIndex < index; gapIndex++)
                {
                    charMatchResults[gapIndex] = true;
                }
            }

            charMatchResults[index] = true;
        }

        gapStart = -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsChineseChar(char ch) => ch is >= '\u4E00' and <= '\u9FA5';

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static char ToLowerAsciiOrInvariant(char ch) =>
        ch is >= 'A' and <= 'Z' ? (char)(ch + 32) : (ch > 127 ? char.ToLowerInvariant(ch) : ch);

    private static int FindNonCjkTokenEnd(string text, int start)
    {
        if (IsPinyinProcessorSplitChar(text[start]))
        {
            return start + 1;
        }

        var end = start + 1;
        while (end < text.Length)
        {
            var ch = text[end];
            if (IsChineseChar(ch) || IsPinyinProcessorSplitChar(ch) || ch is >= 'A' and <= 'Z')
            {
                break;
            }

            end++;
        }

        return end;
    }

    private static bool IsPinyinSearcherSplitChar(char ch) => ch == '`' || IsPinyinProcessorSplitChar(ch);

    private static bool IsPinyinProcessorSplitChar(char ch) => ch switch
    {
        ' ' or '_' or '-' or '.' or ',' or '!' or '@' or '#' or '$' or '%' or '^' or '&' or '*' or '(' or ')' or
        '+' or '=' or '[' or ']' or '{' or '}' or '\\' or '|' or ';' or ':' or '"' or '\'' or '<' or '>' or '?' or
        '/' or '~' => true,
        _ => false
    };

    private static bool MatchesKeywordsInOrderIgnoreCase(string displayName, string[] keywords)
    {
        var index = 0;
        foreach (var keyword in keywords)
        {
            foreach (var ch in keyword)
            {
                if (ch > 127)
                {
                    return false;
                }

                var found = IndexOfIgnoreCase(displayName, ch, index);
                if (found < 0)
                {
                    return false;
                }

                index = found + 1;
            }
        }

        return true;
    }

    private static int IndexOfIgnoreCase(string text, char lowerChar, int startIndex)
    {
        var upperAscii = lowerChar is >= 'a' and <= 'z' ? (char)(lowerChar - 32) : lowerChar;
        for (var i = startIndex; i < text.Length; i++)
        {
            var current = text[i];
            if (current == lowerChar || current == upperAscii || (current > 127 && char.ToLowerInvariant(current) == lowerChar))
            {
                return i;
            }
        }

        return -1;
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

            // Keep existing vectors intact when the optional model plugin is unavailable.
            if ((indexDocuments || indexImages) && !TryGetEmbeddingService(out _))
            {
                UpdateStatus(status => status with { LastError = "lang.kitopia.semantic_model_missing" });
                return;
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
        bool NeedsImageVector);

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
        var metadataMatches = FileStateMatches(existing, fingerprint);
        if (!force && metadataMatches && imageIsCurrent)
        {
            return new ImageIndexWorkItem(
                fullPath,
                fingerprint,
                existing,
                existing!.ContentHash,
                false);
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
            force || !imageIsCurrent || !contentMatches);
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
                    CurrentOperation = "lang.kitopia.updating_image_index",
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
                    await RunPausableStepAsync(token => _store.UpsertFileStateAsync(
                        new FileIndexState(item.Path, IndexFileKind.Image,
                            item.Fingerprint.Length, item.Fingerprint.LastWriteUtcTicks,
                            item.ContentHash, false, null), token), cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    Logger.Warning(exception, "Failed to update image index state for {ImagePath}.", item.Path);
                    UpdateStatus(status => status with { FailedImages = status.FailedImages + 1, LastError = exception.Message });
                }

                MarkCompleted();
            }
        }

        UpdateStatus(status => status with { ProcessingImages = 0, CurrentItem = null });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _disposeCancellation.Cancel();
        Task? loadTask;
        lock (_entriesLock)
        {
            loadTask = _managedFilesLoadTask;
        }

        if (loadTask is not null)
        {
            try
            {
                loadTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
                // Disposal waits for in-flight snapshot reads to close sqlite handles before cleanup.
            }
        }

        _disposeCancellation.Dispose();
        if (_embeddingService is { IsValueCreated: true } initialized)
            initialized.Value?.Dispose();
        _rebuildGate.Dispose();
        _pinyinBuildGate.Dispose();
    }

    private async Task ReleaseIndexingSessionsAsync()
    {
        try
        {
            if (_embeddingService is { IsValueCreated: true, Value: { } embeddingService })
                await embeddingService.ReleaseSessionsAsync();
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

    private bool TryGetEmbeddingService([NotNullWhen(true)] out EmbeddingGemmaEmbeddingService? service)
    {
        var initialization = LazyInitializer.EnsureInitialized(ref _embeddingService,
            () => new Lazy<EmbeddingGemmaEmbeddingService?>(
                () => EmbeddingGemmaEmbeddingService.TryCreate(out var created) ? created : null));
        service = initialization.Value;
        if (service is not null) return true;

        // Missing model files can become available after a download; retry on the next request.
        Interlocked.CompareExchange(ref _embeddingService, null, initialization);
        return false;
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

        if (kitopiaConfig.ignoreItems is null || kitopiaConfig.ignoreItems.Count == 0)
        {
            return [];
        }

        var ignoredPaths = new List<string>();
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
        if (ignoredPaths.Count == 0 || !TryNormalizePath(path, out var normalizedPath))
        {
            return false;
        }

        for (var index = 0; index < ignoredPaths.Count; index++)
        {
            if (IsSameOrDescendantPath(normalizedPath, ignoredPaths[index]))
            {
                return true;
            }
        }

        return false;
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

        if (root.Length == 0 || path.Length <= root.Length || !path.StartsWith(root, PathComparison))
        {
            return false;
        }

        var lastRootChar = root[^1];
        if (lastRootChar == Path.DirectorySeparatorChar || lastRootChar == Path.AltDirectorySeparatorChar)
        {
            return true;
        }

        var separator = path[root.Length];
        return separator == Path.DirectorySeparatorChar || separator == Path.AltDirectorySeparatorChar;
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
        if (!File.Exists(path))
        {
            entry = default;
            return false;
        }

        return TryCreateFileEntry(path, out entry);
    }

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
    private sealed class CjkPinyinData(string[] full, uint fullMask, uint firstMask)
    {
        public string[] Full { get; } = full;
        public uint FullMask { get; } = fullMask;
        public uint FirstMask { get; } = firstMask;
    }

    internal readonly record struct SearchCandidate(
        string DisplayName,
        uint LetterMaskAndFlags,
        uint StartLetterMask)
    {
        public bool HasComplexChars => (LetterMaskAndFlags & ComplexCharsFlag) != 0;
    }

    private sealed class CandidateSnapshot(
        SearchCandidate[] candidates,
        string[] paths,
        SearchEntry[]? explicitEntries = null)
    {
        public static readonly CandidateSnapshot Empty = new([], []);

        public SearchCandidate[] Candidates { get; } = candidates;
        public string[] Paths { get; } = paths;
        public SearchEntry[]? ExplicitEntries { get; } = explicitEntries;
        public int Count => Candidates.Length;
    }

    private readonly record struct QueryContext(
        string[] Keywords,
        uint LetterMask,
        uint StartMask,
        bool HasNonLowerAscii,
        bool IsSingleLowerAsciiChar,
        int[] RemainingAsciiChars,
        double[] RemainingAsciiWeightDeductions);

    private readonly record struct SearchCandidateMatch(int CandidateIndex, double Weight, string Path)
        : IComparable<SearchCandidateMatch>
    {
        public int CompareTo(SearchCandidateMatch other)
        {
            var comparison = Weight.CompareTo(other.Weight);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = other.Path.Length.CompareTo(Path.Length);
            if (comparison != 0)
            {
                return comparison;
            }

            return EntryKeyComparer.Compare(other.Path, Path);
        }
    }

    private readonly record struct MergedCandidateMatch(
        CandidateSnapshot Snapshot,
        int CandidateIndex,
        double Weight,
        string Path)
        : IComparable<MergedCandidateMatch>
    {
        public int CompareTo(MergedCandidateMatch other)
        {
            var comparison = Weight.CompareTo(other.Weight);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = other.Path.Length.CompareTo(Path.Length);
            if (comparison != 0)
            {
                return comparison;
            }

            return EntryKeyComparer.Compare(other.Path, Path);
        }
    }
}
