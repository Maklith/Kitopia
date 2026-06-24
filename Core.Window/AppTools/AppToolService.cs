using Core.Services.Interfaces;
using Core.Window.Everything;
using Pinyin.NET;
using PluginCore;

namespace Core.Window.AppTools;

public class AppToolService : IAppToolService
{
    public void IndexItem(SearchIndex index, string filePath,
        bool isStarred = false)
    {
        AppSolver.IndexItem(index, filePath, isStarred);
    }

    public void CleanupInvalidItems(SearchIndex index)
    {
        AppSolver.CleanupInvalidItems(index);
    }

    public void IndexAllApps(SearchIndex index, bool logging,
        bool useEverything = false)
    {
        AppSolver.IndexAllApps(index, logging, useEverything);
    }

    public void AutoStartEverything(SearchIndex index, Action onSuccess)
    {
        AppSolver.AutoStartEverything(index, onSuccess);
    }

    public IEnumerable<SearchViewItem> SearchWithEverything(string keyword, int limit = 50)
    {
        return EverythingTools.Search(keyword, limit);
    }

    public void LoadIcon(SearchViewItem item)
    {
        IconTools.GetIconByItem(item);
    }

    public void LoadIcon(CustomScenario.CustomScenario item)
    {
        IconTools.GetIconByItem(item);
    }

    public PinyinItem GetPinyin(string input)
    {
        return AppSolver.PinyinProcessor.GetPinyin(input);
    }
}
