using Core.Services.Interfaces;
using Pinyin.NET;
using PluginCore;

namespace Core.Linux;

public class AppToolLinuxService : IAppToolService
{
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

    public void LoadIcon(CustomScenario.CustomScenario item)
    {
    }

    public PinyinItem GetPinyin(string input)
    {
        throw new NotImplementedException();
    }
}
