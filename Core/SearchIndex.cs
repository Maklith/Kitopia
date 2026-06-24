using System;
using System.Collections.Generic;
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
    private PinyinSearcher<SearchEntry>? _searcher;

    public int Count
    {
        get { lock (_lock) { return _entries.Count; } }
    }

    public bool TryAdd(SearchEntry entry)
    {
        lock (_lock)
        {
            if (!_entries.TryAdd(entry.OnlyKey, entry)) return false;
            _searcher = null;
            return true;
        }
    }

    public bool TryRemove(string key)
    {
        lock (_lock)
        {
            if (!_entries.Remove(key)) return false;
            _searcher = null;
            return true;
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
            _searcher = null;
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

            if (keysToRemove.Count > 0)
                _searcher = null;

            return keysToRemove.Count;
        }
    }

    private void EnsureSearcherBuilt()
    {
        lock (_lock)
        {
            if (_searcher is not null) return;
            _searcher = new PinyinSearcher<SearchEntry>(_entries.Values, e => e.DisplayName);
        }
    }

    public void RebuildSearcher()
    {
        lock (_lock)
        {
            _searcher = new PinyinSearcher<SearchEntry>(_entries.Values, e => e.DisplayName);
        }
    }

    public List<SearchResults<SearchEntry>> Search(string query)
    {
        EnsureSearcherBuilt();
        if (string.IsNullOrWhiteSpace(query)) return [];

        PinyinSearcher<SearchEntry> searcher;
        lock (_lock) { searcher = _searcher!; }

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
