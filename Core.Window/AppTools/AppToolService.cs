using System.Collections.Concurrent;
using Core.SDKs.CustomScenario;
using Core.SDKs.Services;
using Core.Services;
using Core.Window.Everything;
using Pinyin.NET;
using PluginCore;

namespace Core.Window;

public class AppToolService : IAppToolService
{
    public void AppSolverA(ConcurrentDictionary<string, SearchViewItem> _collection, string search,
        bool isSearch = false)
    {
        AppTools.AppSolverA(_collection, search, isSearch);
    }

    public void DelNullFile(ConcurrentDictionary<string, SearchViewItem> _collection)
    {
        AppTools.DelNullFile(_collection);
    }

    public void GetAllApps(ConcurrentDictionary<string, SearchViewItem> _collection, bool logging,
        bool useEverything = false)
    {
        AppTools.GetAllApps(_collection, logging, useEverything);
    }

    public void AutoStartEverything(ConcurrentDictionary<string, SearchViewItem> _collection, Action action)
    {
        AppTools.AutoStartEverything(_collection, action);
    }

    public IEnumerable<SearchViewItem> UseEverythingSearch(string s,int limit=50)
    {

        return EverythingTools.Search(s,limit);
    }

    public void GetIconByItem(SearchViewItem item)
    {
        IconTools.GetIconByItem(item);
    }

    public void GetIconByItem(CustomScenario.CustomScenario item)
    {
        IconTools.GetIconByItem(item);
    }

    public PinyinItem GetPinyin(string input)
    {
        return AppTools._pinyinProcessor.GetPinyin(input);
    }
}