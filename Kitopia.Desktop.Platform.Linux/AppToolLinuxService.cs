using Kitopia.Desktop.Features.Search;
using Pinyin.NET;
using PluginCore;

namespace Kitopia.Desktop.Platform.Linux;

public class AppToolLinuxService : IAppToolService
{
    private static readonly PinyinProcessor PinyinProcessor = new();

    public void IndexItem(SearchIndex index, string filePath,
        bool isStarred = false)
    {
    }

    public void CleanupInvalidItems(SearchIndex index)
    {
    }

    public void IndexAllApps(SearchIndex index, bool logging,
        bool useEverything = false)
    {
    }

    public void AutoStartEverything(SearchIndex index, Action onSuccess)
    {
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
