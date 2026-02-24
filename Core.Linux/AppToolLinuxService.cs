using System.Collections.Concurrent;
using Core.Services.Interfaces;
using Pinyin.NET;
using PluginCore;

namespace Core.Linux;

public class AppToolLinuxService : IAppToolService
{
    public void IndexItem(ConcurrentDictionary<string, SearchViewItem> collection, string filePath,
        bool isStarred = false)
    {
    }

    public void CleanupInvalidItems(ConcurrentDictionary<string, SearchViewItem> collection)
    {
    }

    public void IndexAllApps(ConcurrentDictionary<string, SearchViewItem> collection, bool logging,
        bool useEverything = false)
    {
    }

    public void AutoStartEverything(ConcurrentDictionary<string, SearchViewItem> collection, Action onSuccess)
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