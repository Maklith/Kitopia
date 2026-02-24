using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Core.Services.Interfaces;
using Core.Window.Everything;
using Pinyin.NET;
using PluginCore;

namespace Core.Window.AppTools;

public class AppToolService : IAppToolService
{
    public void IndexItem(ConcurrentDictionary<string, SearchViewItem> collection, string filePath,
        bool isStarred = false)
    {
        AppSolver.IndexItem(collection, filePath, isStarred);
    }

    public void CleanupInvalidItems(ConcurrentDictionary<string, SearchViewItem> collection)
    {
        AppSolver.CleanupInvalidItems(collection);
    }

    public void IndexAllApps(ConcurrentDictionary<string, SearchViewItem> collection, bool logging,
        bool useEverything = false)
    {
        AppSolver.IndexAllApps(collection, logging, useEverything);
    }

    public void AutoStartEverything(ConcurrentDictionary<string, SearchViewItem> collection, Action onSuccess)
    {
        AppSolver.AutoStartEverything(collection, onSuccess);
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