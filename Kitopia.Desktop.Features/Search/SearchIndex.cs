using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kitopia.Desktop.Features.Indexing;
using Pinyin.NET;
using PluginCore;

namespace Kitopia.Desktop.Features.Search;

public readonly record struct SearchEntry
{
    public string DisplayName { get; init; }
    public string OnlyKey { get; init; }
    public FileType FileType { get; init; }
    public string? Arguments { get; init; }
    public string? StartDirectory { get; init; }
    public string? LaunchPath { get; init; }
    public string? IconPath { get; init; }
    public int IconSymbol { get; init; }

    public SearchViewItem ToSearchViewItem()
    {
        return new SearchViewItem
        {
            ItemDisplayName = DisplayName,
            OnlyKey = OnlyKey,
            FileType = FileType,
            Arguments = Arguments,
            StartDirectory = StartDirectory,
            LaunchPath = LaunchPath,
            IconPath = IconPath,
            IconSymbol = IconSymbol,
            IsVisible = true
        };
    }
}

public class SearchIndex : ISearchEntryIndex
{
    // Compatibility-only pinyin index for short-lived callers. Process-wide semantic retrieval is
    // owned exclusively by IIndexService and its sqlite-vec index.db store.
    private const int SemanticFallbackPinyinResultLimit = 10;
    private const int MinimumSemanticQueryLength = 2;
    private readonly Dictionary<string, SearchEntry> _entries = new();
    private readonly object _lock = new();
    private volatile PinyinSearcher<SearchEntry>? _searcher;
    private int _rebuildVersion;

    public int Count
    {
        get { lock (_lock) { return _entries.Count; } }
    }

    public bool TryAdd(SearchEntry entry)
    {
        var added = false;
        lock (_lock)
        {
            added = _entries.TryAdd(entry.OnlyKey, entry);
        }

        return added;
    }

    /// <summary>
    /// Adds a single entry and refreshes the searchable snapshot when the entry was accepted.
    /// Bulk indexers should continue to use <see cref="TryAdd(SearchEntry)"/> and rebuild once.
    /// </summary>
    public bool TryAddAndRefreshSearcher(SearchEntry entry)
    {
        if (!TryAdd(entry)) return false;

        RebuildSearcher();
        return true;
    }

    public bool TryRemove(string key)
    {
        var removed = false;
        lock (_lock)
        {
            removed = _entries.Remove(key);
        }

        return removed;
    }

    /// <summary>
    /// Removes a single entry and refreshes the searchable snapshot when the entry existed.
    /// Bulk indexers should continue to use <see cref="TryRemove(string)"/> and rebuild once.
    /// </summary>
    public bool TryRemoveAndRefreshSearcher(string key)
    {
        if (!TryRemove(key)) return false;

        RebuildSearcher();
        return true;
    }

    public bool TryGetValue(string key, out SearchEntry entry)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(key, out entry);
        }
    }

    public bool ContainsKey(string key)
    {
        lock (_lock)
        {
            return _entries.ContainsKey(key);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }

    public int RemoveWhere(Func<string, SearchEntry, bool> predicate)
    {
        List<string> keysToRemove;
        lock (_lock)
        {
            keysToRemove = new List<string>();
            foreach (var (key, entry) in _entries)
                if (predicate(key, entry))
                    keysToRemove.Add(key);

            foreach (var key in keysToRemove)
                _entries.Remove(key);

        }

        return keysToRemove.Count;
    }

    public void RebuildSearcher()
    {
        List<SearchEntry> snapshot;
        lock (_lock)
        {
            snapshot = new List<SearchEntry>(_entries.Values);
        }

        var version = Interlocked.Increment(ref _rebuildVersion);
        Task.Run(() =>
        {
            var newSearcher = new PinyinSearcher<SearchEntry>(snapshot, e => e.DisplayName);
            if (_rebuildVersion == version)
                _searcher = newSearcher;
        });
    }

    public void AppendToSearcher(IEnumerable<SearchEntry> entries)
    {
        var entriesToAppend = entries.ToList();
        if (_searcher is null)
        {
            RebuildSearcher();
            return;
        }

        _searcher.AppendLoad(entriesToAppend, e => e.DisplayName);
    }

    public List<SearchResults<SearchEntry>> Search(
        string query,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || maximumResults <= 0) return [];
        cancellationToken.ThrowIfCancellationRequested();

        var searcher = _searcher;
        if (searcher is null) return [];

        return searcher.Search(query, maximumResults, cancellationToken).ToList();
    }

    public List<SearchResults<SearchEntry>> Search(string query)
    {
        return Search(query, int.MaxValue, CancellationToken.None);
    }

    public static bool ShouldSearchSemantically(string query, int pinyinResultCount)
    {
        return pinyinResultCount < SemanticFallbackPinyinResultLimit
               && query.Trim().Length >= MinimumSemanticQueryLength;
    }

    public Task<List<SearchIndexResult>> SearchSemanticallyAsync(
        string query,
        IReadOnlyList<SearchResults<SearchEntry>> pinyinResults,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var merged = new Dictionary<string, SearchIndexResult>(StringComparer.Ordinal);
        for (var index = 0; index < pinyinResults.Count; index++)
        {
            var result = pinyinResults[index];
            merged[result.Source.OnlyKey] = new SearchIndexResult(
                result.Source,
                1d / (60 + index + 1),
                result.CharMatchResults);
        }

        return Task.FromResult(merged.Values.OrderByDescending(result => result.Weight).ToList());
    }

    public IReadOnlyList<KeyValuePair<string, SearchEntry>> GetEntriesSnapshot()
    {
        lock (_lock)
        {
            return new List<KeyValuePair<string, SearchEntry>>(_entries);
        }
    }
}

public sealed record SearchIndexResult(
    SearchEntry Source,
    double Weight,
    bool[]? CharMatchResults,
    int? SemanticContentChunkIndex = null);
