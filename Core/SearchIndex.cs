using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Pinyin.NET;
using PluginCore;

namespace Core;

public readonly record struct SearchEntry
{
    public string DisplayName { get; init; }
    public string OnlyKey { get; init; }
    public FileType FileType { get; init; }
    public string? Arguments { get; init; }
    public string? StartDirectory { get; init; }
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
            IconPath = IconPath,
            IconSymbol = IconSymbol,
            IsVisible = true
        };
    }
}

public class SearchIndex
{
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
        lock (_lock)
        {
            return _entries.TryAdd(entry.OnlyKey, entry);
        }
    }

    public bool TryRemove(string key)
    {
        lock (_lock)
        {
            return _entries.Remove(key);
        }
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
        lock (_lock)
        {
            var keysToRemove = new List<string>();
            foreach (var (key, entry) in _entries)
                if (predicate(key, entry))
                    keysToRemove.Add(key);

            foreach (var key in keysToRemove)
                _entries.Remove(key);

            return keysToRemove.Count;
        }
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
        if (_searcher is null)
        {
            RebuildSearcher();
            return;
        }

        _searcher.AppendLoad(entries, e => e.DisplayName);
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

    public List<KeyValuePair<string, SearchEntry>> GetEntriesSnapshot()
    {
        lock (_lock)
        {
            return new List<KeyValuePair<string, SearchEntry>>(_entries);
        }
    }
}
