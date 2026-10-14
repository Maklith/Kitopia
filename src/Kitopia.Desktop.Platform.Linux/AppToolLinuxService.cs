using Kitopia.Desktop.Features.Search;
using Kitopia.Desktop.Features.Indexing;
using Pinyin.NET;
using PluginCore;

namespace Kitopia.Desktop.Platform.Linux;

public class AppToolLinuxService : IAppToolService
{
    private static readonly PinyinProcessor PinyinProcessor = new();

    public void IndexItem(ISearchEntryIndex index, string filePath,
        bool isStarred = false)
    {
    }

    public void CleanupInvalidItems(IIndexService index)
    {
    }

    public void IndexAllApps(IIndexService index, bool logging,
        bool useEverything = false)
    {
    }

    public void AutoStartEverything(IIndexService index, Action onSuccess)
    {
    }

    public void VisitEverythingIndexedFiles(Action<string> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
    }

    public IEnumerable<string> EnumerateEverythingIndexedFiles()
    {
        yield break;
    }

    public IEnumerable<SearchViewItem> SearchWithEverything(string keyword, int limit = 50)
    {
        return Array.Empty<SearchViewItem>();
    }

    public void LoadIcon(SearchViewItem item)
    {
    }

    public void LoadIcon(Kitopia.Desktop.Features.CustomScenario.CustomScenario item)
    {
    }

    public void LoadIcon(string filePath, Action<Avalonia.Media.Imaging.Bitmap?> callback)
    {
        callback(null);
    }

    public byte[]? GetFileIconPng(string filePath)
    {
        return null;
    }

    public PinyinItem GetPinyin(string input)
    {
        return PinyinProcessor.GetPinyin(input);
    }
}
