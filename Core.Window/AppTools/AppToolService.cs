using System.Collections.Concurrent;
using Core.Services.Interfaces;
using Core.Window.Everything;
using Pinyin.NET;
using PluginCore;

namespace Core.Window.AppTools;

public class AppToolService : IAppToolService
{
    public void AppSolverA(ConcurrentDictionary<string, SearchViewItem> collection, string search,
        bool isSearch = false)
    {
        AppSolver.AppSolverA(collection, search, isSearch);
    }

    public void DelNullFile(ConcurrentDictionary<string, SearchViewItem> collection)
    {
        AppSolver.DelNullFile(collection);
    }

    public void GetAllApps(ConcurrentDictionary<string, SearchViewItem> collection, bool logging,
        bool useEverything = false)
    {
        AppSolver.GetAllApps(collection, logging, useEverything);
    }

    public void AutoStartEverything(ConcurrentDictionary<string, SearchViewItem> collection, Action action)
    {
        AppSolver.AutoStartEverything(collection, action);
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
        return AppSolver.PinyinProcessor.GetPinyin(input);
    }
}