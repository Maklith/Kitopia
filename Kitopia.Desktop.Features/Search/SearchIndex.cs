using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kitopia.Desktop.Features.Search.Semantic;
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

public class SearchIndex
{
    // Lexical matches are immediate and precise enough for ordinary app and file-name searches.
    // Reserving the expensive embedding lookup for sparse matches keeps RAG from competing with typing.
    private const int SemanticFallbackPinyinResultLimit = 10;
    private readonly Dictionary<string, SearchEntry> _entries = new();
    private readonly object _lock = new();
    private readonly SemanticSearchIndex _semanticIndex = new();
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

        if (added)
        {
            _semanticIndex.Upsert(entry);
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

        if (removed)
        {
            _semanticIndex.Remove(key);
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
        List<string> keys;
        lock (_lock)
        {
            keys = new List<string>(_entries.Keys);
            _entries.Clear();
        }

        foreach (var key in keys)
        {
            _semanticIndex.Remove(key);
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

        foreach (var key in keysToRemove)
        {
            _semanticIndex.Remove(key);
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

        _semanticIndex.Synchronize(snapshot);

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
        foreach (var entry in entriesToAppend)
        {
            _semanticIndex.Upsert(entry);
        }
    }

    public List<SearchResults<SearchEntry>> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var searcher = _searcher;
        if (searcher is null) return [];

        var results = new List<SearchResults<SearchEntry>>();
        foreach (var r in searcher.Search(query))
            results.Add(r);

        return results;
    }

    public async Task<List<SearchIndexResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        return await SearchAsync(query, Search(query), cancellationToken);
    }

    public async Task<List<SearchIndexResult>> SearchAsync(
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

        if (pinyinResults.Count >= SemanticFallbackPinyinResultLimit)
        {
            return merged.Values.OrderByDescending(result => result.Weight).ToList();
        }

        try
        {
            var semanticResults = await _semanticIndex.SearchAsync(query, cancellationToken);
            for (var index = 0; index < semanticResults.Count; index++)
            {
                var semanticResult = semanticResults[index];
                if (!TryGetValue(semanticResult.OnlyKey, out var entry))
                {
                    continue;
                }

                // Keep rank decay for reciprocal-rank fusion while preserving the cosine similarity
                // returned by the vector store. Negative similarities are not relevant search signals.
                var semanticScore = Math.Max(0d, semanticResult.Score) / (60 + index + 1);
                if (merged.TryGetValue(semanticResult.OnlyKey, out var existing))
                {
                    merged[semanticResult.OnlyKey] = existing with
                    {
                        Weight = existing.Weight + semanticScore,
                        SemanticContentChunkIndex = semanticResult.ContentChunkIndex
                                                   ?? existing.SemanticContentChunkIndex
                    };
                }
                else
                {
                    merged[semanticResult.OnlyKey] = new SearchIndexResult(
                        entry,
                        semanticScore,
                        null,
                        semanticResult.ContentChunkIndex);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // SemanticSearchIndex logs indexing failures. Pinyin search remains the fallback.
        }

        return merged.Values.OrderByDescending(result => result.Weight).ToList();
    }

    public List<KeyValuePair<string, SearchEntry>> GetEntriesSnapshot()
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
