using System.Collections.Concurrent;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Kitopia.Desktop.Features.Search;
using Pinyin.NET;
using PluginCore;

namespace KitopiaBenchmark;

[Config(typeof(InProcessConfig))]
[MemoryDiagnoser]
public class OldVsNewMemory
{
    [Params(1000, 5000, 10000)]
    public int N;

    private string[] _keys = null!;
    private string[] _names = null!;

    [GlobalSetup]
    public void Setup()
    {
        _keys = new string[N];
        _names = new string[N];
        for (var i = 0; i < N; i++)
        {
            var name = $"应用程序{i:D8}";
            _keys[i] = $@"C:\Program Files\AppSuite\{name}\{name}.exe";
            _names[i] = name;
        }
    }

    [Benchmark(Description = "Old: ConcurrentDict+SearchViewItem")]
    public object Old()
    {
        var dict = new ConcurrentDictionary<string, SearchViewItem>();
        for (var i = 0; i < N; i++)
            dict.TryAdd(_keys[i], new SearchViewItem
            {
                ItemDisplayName = _names[i], OnlyKey = _keys[i], FileType = FileType.应用程序, IsVisible = true
            });
        return dict;
    }

    [Benchmark(Description = "Old: +PinyinSearcher")]
    public object OldWithSearcher()
    {
        var dict = new ConcurrentDictionary<string, SearchViewItem>();
        for (var i = 0; i < N; i++)
            dict.TryAdd(_keys[i], new SearchViewItem
            {
                ItemDisplayName = _names[i], OnlyKey = _keys[i], FileType = FileType.应用程序, IsVisible = true
            });
        return new PinyinSearcher<KeyValuePair<string, SearchViewItem>>(dict, e => e.Value.ItemDisplayName);
    }

    [Benchmark(Description = "New: SearchIndex(SearchEntry)")]
    public SearchIndex New()
    {
        var idx = new SearchIndex();
        for (var i = 0; i < N; i++)
            idx.TryAdd(new SearchEntry
            {
                DisplayName = _names[i], OnlyKey = _keys[i], FileType = FileType.应用程序
            });
        return idx;
    }

    [Benchmark(Description = "New: +Searcher")]
    public SearchIndex NewWithSearcher()
    {
        var idx = new SearchIndex();
        for (var i = 0; i < N; i++)
            idx.TryAdd(new SearchEntry
            {
                DisplayName = _names[i], OnlyKey = _keys[i], FileType = FileType.应用程序
            });
        idx.Search("App");
        return idx;
    }
}

public class InProcessConfig : ManualConfig
{
    public InProcessConfig()
    {
        AddJob(Job.ShortRun
            .WithToolchain(InProcessEmitToolchain.Instance)
            .WithId("InProcess"));
    }
}
