using System.Collections.Concurrent;
using Pinyin.NET;
using PluginCore;

namespace Core.Services;

public interface IAppToolService
{
    public void AppSolverA(ConcurrentDictionary<string, SearchViewItem> _collection, string search,
        bool isSearch = false);

    public void DelNullFile(ConcurrentDictionary<string, SearchViewItem> _collection);

    public void GetAllApps(ConcurrentDictionary<string, SearchViewItem> _collection, bool logging,
        bool useEverything = false);

    public void AutoStartEverything(ConcurrentDictionary<string, SearchViewItem> _collection, Action action);
    public IEnumerable<SearchViewItem> UseEverythingSearch(string s, int limit = 50);
    public void GetIconByItem(SearchViewItem item);
    public void GetIconByItem(CustomScenario.CustomScenario item);
    public PinyinItem GetPinyin(string input);
}