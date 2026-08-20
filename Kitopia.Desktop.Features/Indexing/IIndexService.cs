using Kitopia.Desktop.Features.Search;
using PluginCore;

namespace Kitopia.Desktop.Features.Indexing;

/// <summary>
/// Owns every searchable index. UI surfaces may query it, but do not own an index or an ONNX session.
/// </summary>
public interface ISearchEntryIndex
{
    bool TryAdd(SearchEntry entry);

    bool ContainsKey(string onlyKey);

    IReadOnlyList<KeyValuePair<string, SearchEntry>> GetEntriesSnapshot();
}

public interface IIndexService : ISearchEntryIndex
{
    event EventHandler<IndexStatusSnapshot>? StatusChanged;

    IndexStatusSnapshot GetStatus();

    bool TryAdd(SearchEntry entry, IndexSource source = IndexSource.Application);

    bool TryRemove(string onlyKey);

    bool TryGetValue(string onlyKey, out SearchEntry entry);

    int RemoveWhere(Func<string, SearchEntry, bool> predicate);

    void Synchronize(IEnumerable<SearchEntry> entries, IndexSource source = IndexSource.Application);

    /// <summary>
    /// Streams a managed-file discovery pass into the persistent manifest. The enumerable is
    /// consumed once and is never materialized as a complete path list.
    /// </summary>
    Task<bool> SynchronizeFilesAsync(
        IEnumerable<string> paths,
        IndexSource source,
        CancellationToken cancellationToken = default);

    void RebuildPinyinSearcher();

    Task RebuildPinyinSearcherAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<SearchIndexResult> SearchPinyin(
        string query,
        int maximumResults,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SearchIndexResult>> SearchAsync(
        string query,
        int maximumResults,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates file-backed document and image vectors without clearing the existing index.
    /// </summary>
    Task IndexIncrementalAsync(IndexRebuildScope scope, CancellationToken cancellationToken = default);

    Task RebuildAsync(IndexRebuildScope scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all persisted file manifests, vectors, and file fingerprints. Search entries and
    /// user configuration remain intact and can be discovered again after the reset.
    /// </summary>
    Task ResetAsync(CancellationToken cancellationToken = default);

    void PauseIndexing();

    void ResumeIndexing();

    void CancelIndexing();
}

/// <summary>
/// Owns refreshes of index sources. Search UI may request a refresh, but never owns entries or vectors.
/// </summary>
public interface IIndexMaintenanceService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task RefreshEverythingFilesAsync(CancellationToken cancellationToken = default);

    Task RefreshManagedFilesAsync(CancellationToken cancellationToken = default);

    Task StopBackgroundIndexingAsync();

    void RefreshWindowOpenEntries();
}

public enum IndexSource
{
    Application,
    Plugin,
    Manual,
    Document,
    Image,
    EverythingManaged
}

public enum IndexRebuildScope
{
    All,
    Pinyin,
    Documents,
    Images,
    Files
}
