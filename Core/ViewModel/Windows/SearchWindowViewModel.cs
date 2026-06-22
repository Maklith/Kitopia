#region

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using AvaloniaEdit.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Core.Services;
using Core.Services.Config;
using Core.Services.Interfaces;
using Core.Services.Plugin;
using ObservableCollections;
using Pinyin.NET;
using PluginCore;
using PluginCore.SearchWindow.InputData;
using PluginCore.SearchWindow.InputDataAnalyzer;
using ReactiveUI;
using Serilog;

#endregion

namespace Core.ViewModel.Windows;

/// <summary>
/// 文件类型过滤器 / File type filter for search results
/// </summary>
public class FileTypeFilter
{
    /// <summary>获取或设置文件类型 / Gets or sets the file type</summary>
    public FileType FileType { get; set; }

    /// <summary>获取或设置是否选中 / Gets or sets whether the filter is checked</summary>
    public bool IsChecked { get; set; }
}

/// <summary>
/// 搜索窗口视图模型 / Search window view model for handling search functionality
/// </summary>
public partial class SearchWindowViewModel : ObservableRecipient
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<SearchWindowViewModel>();
    public readonly ConcurrentDictionary<string, SearchViewItem> IndexCollection = new(); //存储本机所有软件

    [ObservableProperty] private bool? _everythingIsOk = true;


    [ObservableProperty] private ObservableCollection<FileTypeFilter> _fileTypes = new();

    [ObservableProperty] private ObservableCollection<InputData> _inputDatas = new();
    [ObservableProperty] private ObservableList<SearchViewItem> _items = new(100);
    [ObservableProperty] private ObservableCollection<SearchViewItem> _pinnedItems = new();
    [ObservableProperty] private ISynchronizedView<SearchViewItem, SearchViewItem> _itemsView;
    [ObservableProperty] private NotifyCollectionChangedSynchronizedViewList<SearchViewItem> _itemsViewList;


    [ObservableProperty] private bool _nowInSelectMode;
    private PinyinSearcher<KeyValuePair<string, SearchViewItem>>? _pinyinSearcher;

    private bool _reloading;
    private int _loadLastRequestId;
    private int _loadLastAppliedId;
    private int _loadLastScheduled;


    [ObservableProperty] private string _search=string.Empty;
    private Action<SearchViewItem?>? _selectAction;


    [ObservableProperty] private int? _selectedIndex = -1;

    [ObservableProperty] private bool _showFileTypeFilter;
    [ObservableProperty] private bool _showPinnedItems;
    [ObservableProperty] private bool _showInputData;

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }

    public SearchWindowViewModel()
    {
        ItemsView = Items.CreateView(e => e);
        ItemsViewList = ItemsView.ToNotifyCollectionChanged();
        Task.Run(() =>
        {
            ReloadApps();
            LoadLast();
        }).ContinueWith(e =>
        {
            if (e.Exception is not null) Logger.Error(e.Exception, "");
        });
        this.WhenAnyValue(e => e.Search)
            .Throttle(TimeSpan.FromMilliseconds(100))
            .DistinctUntilChanged()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(ToSearch, e => { Logger.Error(e, ""); });
    }


    private readonly Dictionary<object, List<string>> _analyzerIndexedKeys = new();
    
    public void RemoveAnalyzerIndex(object analyzer)
    {
        if (_analyzerIndexedKeys.TryGetValue(analyzer, out var keys))
        {
            foreach (var key in keys)
            {
                if (IndexCollection.TryRemove(key, out var item))
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Items.Remove(item);
                        PinnedItems.Remove(item);
                    });
                }
            }
            _analyzerIndexedKeys.Remove(analyzer);
            
            _pinyinSearcher =
                new PinyinSearcher<KeyValuePair<string, SearchViewItem>>(IndexCollection, e => e.Value.ItemDisplayName);
        }
    }
    
    public void UpdateIndexOnWindowOpen()
    {
        var changed = false;
        foreach (var (_, analyzers) in PluginOverall.SearchWindowInputDataAnalyzers)
        foreach (var analyzerTuple in analyzers)
        {
            var timeFlags = analyzerTuple.Item1();
            if ((timeFlags & InputDataAnalyzeTimeFlags.WindowOpenUpdateIndex) != 0)
            {
                // 1. Remove previous items
                if (_analyzerIndexedKeys.TryGetValue(analyzerTuple, out var oldKeys))
                {
                    foreach (var key in oldKeys) IndexCollection.TryRemove(key, out _);
                    changed = true;
                }

                // 2. Generate new items
                var newItems = analyzerTuple.Item2(new List<InputData>()).ToList();
                var newKeys = new List<string>();

                // 3. Add new items
                foreach (var item in newItems)
                    if (IndexCollection.TryAdd(item.OnlyKey, item))
                        newKeys.Add(item.OnlyKey);

                _analyzerIndexedKeys[analyzerTuple] = newKeys;
                if (newKeys.Count > 0) changed = true;
            }
        }

        if (changed)
        {
            _pinyinSearcher =
                new PinyinSearcher<KeyValuePair<string, SearchViewItem>>(IndexCollection, e => e.Value.ItemDisplayName);
        }
    }
    
    public void ReloadApps(bool logging = false)
    {
        if (_reloading) return;


        _reloading = true;
        CheckEverything();
        ServiceManager.Services.GetService<IAppToolService>()!.CleanupInvalidItems(IndexCollection);
        ServiceManager.Services.GetService<IAppToolService>()!.IndexAllApps(IndexCollection, logging,
            ConfigManger.Config.useEverything);
        if (_pinyinSearcher is null)
        {
            _pinyinSearcher =
                new PinyinSearcher<KeyValuePair<string, SearchViewItem>>(IndexCollection, e => e.Value.ItemDisplayName);
        }
        else
        {
            _pinyinSearcher.AppendLoad(IndexCollection, e => e.Value.ItemDisplayName);
        }


        _reloading = false;
    }

    private void CheckEverything()
    {
        if (ConfigManger.Config.useEverything && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Logger.Debug("everything检测");


            var service = ServiceManager.Services.GetService<IEverythingService>()!;
            RunOnUiThread(() => { EverythingIsOk = service.IsRun(); });

            if (!EverythingIsOk.Value)
                ServiceManager.Services.GetService<IAppToolService>()!.AutoStartEverything(IndexCollection, () =>
                {
                    Thread.Sleep(1500);
                    var everythingService = ServiceManager.Services.GetService<IEverythingService>()!;
                    RunOnUiThread(() => { EverythingIsOk = everythingService.IsRun(); });
                });
        }
    }


    public void LoadLast()
    {
        Interlocked.Increment(ref _loadLastRequestId);

        if (Interlocked.Exchange(ref _loadLastScheduled, 1) == 1)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                while (Volatile.Read(ref _loadLastAppliedId) != Volatile.Read(ref _loadLastRequestId))
                {
                    Volatile.Write(ref _loadLastAppliedId, Volatile.Read(ref _loadLastRequestId));
                    LoadLastCore();
                }
            }
            finally
            {
                Volatile.Write(ref _loadLastScheduled, 0);

                if (Volatile.Read(ref _loadLastAppliedId) != Volatile.Read(ref _loadLastRequestId))
                {
                    LoadLast();
                }
            }
        });
    }

    private void LoadLastCore()
    {
        if (!string.IsNullOrEmpty(Search)) return;
        
        
        Logger.Debug("加载历史记录");


        Items.Clear();
        PinnedItems.Clear();

        var limit = 0;
        //Items.RaiseListChangedEvents = false;
        if (ConfigManger.Config.alwayShows.Any())
        {
            Logger.Debug("加载常驻");
            foreach (var configAlwayShow in ConfigManger.Config.alwayShows)
                if (IndexCollection.TryGetValue(configAlwayShow, out var searchViewItem))
                {
                    var item = searchViewItem;

                    Logger.Debug("加载常驻:" + item.OnlyKey);


                    item.IsPined = true;
                    //Items.Add(item);
                    PinnedItems.Add(item);


                    limit++;
                }
        }


        if (ConfigManger.Config.lastOpens.Any())
        {
            Logger.Debug("加载历史");
            var sortedDict = ConfigManger.Config.lastOpens
                .Select(p => new
                {
                    p.Key,
                    p.Value,
                    Score = p.Value.AccessTimes.Sum(t => 1.0 / (1.0 + (DateTime.Now - t).TotalDays))
                })
                .Where(p => p.Score > 0)
                .OrderByDescending(p => p.Score)
                .ToDictionary(p => p.Key, p => p.Value);
            foreach (var (key, _) in sortedDict)
            {
                if (limit >= ConfigManger.Config.maxHistory)
                {
                    Logger.Debug("超过历史记录限制,当前" + limit);


                    break;
                }

                if (IndexCollection.TryGetValue(key, out var item))
                {
                    Logger.Debug("加载历史:" + item.OnlyKey);


                    if (!Items.Any((e) => e.OnlyKey.Equals(item.OnlyKey)))
                    {
                        if (PinnedItems.All(e => e.OnlyKey != item.OnlyKey))
                        {
                            PinnedItems.Add(item);
                            limit++;
                        }

                    }
                }
            }
        }
        ProcessInputData(null, InputDataAnalyzeTimeFlags.InputEmpty);
        ShowPinnedItems = PinnedItems.Count > 0;
        FileTypes.Clear();
        ShowFileTypeFilter = FileTypes.Count > 0;
        foreach (var searchViewItem in Items)
        {
            searchViewItem.PinyinItem = null;
        }
    }

    private readonly List<SearchViewItem> _lastSearchItems = new();
    public void ProcessInputData(string? value, InputDataAnalyzeTimeFlags nowTimeFlags)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ProcessInputData(value, nowTimeFlags));
            return;
        }

        foreach (var lastSearchItem in _lastSearchItems)
        {
            PinnedItems.Remove(lastSearchItem);
            Items.Remove(lastSearchItem);
        }
        _lastSearchItems.Clear();
        List<InputData> inputDatas = new List<InputData>();
        
        InputDatas.Clear();
        foreach (var (_, funcs) in PluginOverall.SearchWindowInputDataIdentifies)
        foreach (var func in funcs)
        {
            var inputData = func.Invoke(nowTimeFlags, value);
            inputDatas.AddRange(inputData);
        }

        if (!string.IsNullOrWhiteSpace(value))
            inputDatas.Add(new InputData
            {
                InputType = InputType.文本,
                Data = value
            });
        
        InputDatas.AddRange(inputDatas);
        ShowInputData = InputDatas.Count > 0;
        
        foreach (var (_, funcs) in PluginOverall.SearchWindowInputDataAnalyzers)
        foreach (var func in funcs)
        {
            var inputDataAnalyzeTimeFlags = func.Item1.Invoke();
            if ((inputDataAnalyzeTimeFlags & nowTimeFlags) == 0) continue; // 如果当前时间标志不匹配，则跳过
            var enumerable = func.Item2.Invoke(InputDatas).ToList();
            _lastSearchItems.AddRange(enumerable);
            foreach (var searchViewItem in enumerable)
            {
                if (searchViewItem.ShowAsMiniApp)
                {
                    PinnedItems.Insert(0, searchViewItem);
                }else
                    Items.Insert(0, searchViewItem);
            }
        }
        ShowPinnedItems = PinnedItems.Count > 0;
    }

    // ReSharper disable once RedundantAssignment
    public void ToSearch(string? value)
    {
        //Log.Debug("搜索开始");
        if (string.IsNullOrEmpty(value))
        {
            LoadLast();
            
            return;
        }
        ShowPinnedItems = false;

        Logger.Debug("搜索变更:" + value);
        // Items.RaiseListChangedEvents = false;

        #region 清除上次搜索结果

        Items.Clear();
        PinnedItems.Clear();


        #endregion

        ProcessInputData(value, InputDataAnalyzeTimeFlags.InputChanged);

        var originalValue = value;
        value = value.ToLowerInvariant();
        var pluginItem = 0;


        if (originalValue.StartsWith(ConfigManger.Config.everythingSearchPreString) &&
            originalValue.Length > ConfigManger.Config.everythingSearchPreString.Length)
        {
            var useEverythingSearch = ServiceManager.Services.GetService<IAppToolService>()
                .SearchWithEverything(originalValue.Remove(0, ConfigManger.Config.everythingSearchPreString.Length),
                    ConfigManger.Config.everythingSearchMaxCount);
            Items.AddRange(useEverythingSearch);
        }
        else
        {
            #region 从文件索引检索并排序

            var filtered = _pinyinSearcher?.Search(value)
                .ToList();

            var sorted = filtered?.OrderByDescending(x => x.Weight).Select(e =>
                {
                    e.Source.Value.PinyinItem = e.CharMatchResults;
                    return e;
                })
                .ToList();

            #endregion

            if (sorted is null)
            {
                return;
            }

            var count = 0; // 计数器变量
            const int limit = 100; // 限制次数
            Dictionary<SearchViewItem, int> nowHasLastOpens = new();

            for (var i = sorted.Count - 1; i >= 0; i--)
                if (ConfigManger.Config.lastOpens.TryGetValue(sorted[i].Source.Value.OnlyKey, out _))
                {
                    nowHasLastOpens.Add(sorted[i].Source.Value, (int)sorted[i].Weight);
                    sorted.RemoveAt(i);
                }

            var sortedDict = nowHasLastOpens.OrderByDescending(p => p.Value)
                .ToDictionary(p => p.Key, p => p.Value);
            var resultsToAdd = new List<SearchViewItem>(Math.Min(sortedDict.Count + sorted.Count, limit));
            foreach (var (searchViewItem, _) in sortedDict)
            {
                if (ConfigManger.Config.alwayShows.Contains(searchViewItem.OnlyKey)) searchViewItem.IsPined = true;
                count++;
                resultsToAdd.Add(searchViewItem);
            }

            foreach (var x in sorted)
            {
                if (count >= limit) break;

                var searchViewItem = x.Source.Value;
                if (ConfigManger.Config.alwayShows.Contains(searchViewItem.OnlyKey)) searchViewItem.IsPined = true;
                resultsToAdd.Add(searchViewItem);
                searchViewItem.Notify();
                count++;
            }

            Items.AddRange(resultsToAdd);
        }


        if (Items.Count <= pluginItem)
        {
            Logger.Debug("无搜索项目,添加网页搜索");
            Items.AddRange(new[]
            {
                new SearchViewItem
                {
                    ItemDisplayName = "将内容添加至便签" + originalValue,
                    FileType = FileType.便签,
                    OnlyKey = originalValue,
                    Icon = null,
                    IconSymbol = 0xF6EC,
                    IsVisible = true
                },
                new SearchViewItem
                {
                    ItemDisplayName = "在网页中搜索" + originalValue,
                    FileType = FileType.URL,
                    OnlyKey = "https://www.bing.com/search?q=" + originalValue,
                    Icon = null,
                    IconSymbol = 62555,
                    IsVisible = true
                }
            });
        }

        Dispatcher.UIThread.Post(() => {
            FileTypes.Clear();
            var fileTypes = Items.Select(e => e.FileType).Distinct();
            foreach (var fileType in fileTypes)
                FileTypes.Add(new FileTypeFilter
                {
                    FileType = fileType,
                    IsChecked = false
                });

            ShowFileTypeFilter = FileTypes.Count > 0;
        });
        
    }
    

    public void SetSelectMode(bool flag, Action<SearchViewItem?> action)
    {
        NowInSelectMode = flag;
        _selectAction = action;
    }

    [RelayCommand]
    public void OpenFile(SearchViewItem? item)
    {
        if (item is null)
        {
            return;
        }
        Task.Run(() =>
        {
            if (NowInSelectMode)
            {
                _selectAction?.Invoke(item);
                NowInSelectMode = false;
                WeakReferenceMessenger.Default.Send("a", "SearchWindowClose");
                return;
            }

            ServiceManager.Services.GetService<ISearchItemTool>()!.OpenFile(item,Search);
            WeakReferenceMessenger.Default.Send("a", "SearchWindowClose");
        });
        
        Search = "";
    }

    [RelayCommand]
    private void IgnoreItem(SearchViewItem searchViewItem)
    {
        Dispatcher.UIThread.InvokeAsync(() => { Items.Remove(searchViewItem); });
        ServiceManager.Services.GetService<ISearchItemTool>()!.IgnoreItem(searchViewItem);
    }

    [RelayCommand]
    private void OpenFolder(object searchViewItem)
    {
        Search = "";
        ServiceManager.Services.GetService<ISearchItemTool>()!.OpenFolder((SearchViewItem?)searchViewItem);
        WeakReferenceMessenger.Default.Send("a", "SearchWindowClose");
    }

    [RelayCommand]
    private void RunAsAdmin(object searchViewItem)
    {
        Search = "";
        ServiceManager.Services.GetService<ISearchItemTool>()!.RunAsAdmin((SearchViewItem?)searchViewItem);
        WeakReferenceMessenger.Default.Send("a", "SearchWindowClose");
    }

    [RelayCommand]
    private void Star(SearchViewItem item)
    {
        ServiceManager.Services.GetService<ISearchItemTool>()!.Star(item);
    }

    [RelayCommand]
    private void Pin(object searchViewItem)
    {
        var item = (SearchViewItem)searchViewItem;
        Logger.Debug("添加常驻" + item.OnlyKey);
        //Items.ResetItem(index);

        ServiceManager.Services.GetService<ISearchItemTool>()!.Pin(item);
        if (item.IsPined)
        {
            if (!PinnedItems.Contains(item)) PinnedItems.Insert(0, item);
        }
        else
        {
            if (PinnedItems.Contains(item)) PinnedItems.Remove(item);
        }

        if (string.IsNullOrEmpty(Search)) ShowPinnedItems = PinnedItems.Count > 0;
    }

    [RelayCommand]
    private void OpenFolderInTerminal(object searchViewItem)
    {
        Search = "";
        ServiceManager.Services.GetService<ISearchItemTool>()!.OpenFolderInTerminal((SearchViewItem?)searchViewItem);
        WeakReferenceMessenger.Default.Send("a", "SearchWindowClose");
    }


    public void UpdateFilter()
    {
        if (FileTypes.All(e => !e.IsChecked))
            ItemsView.ResetFilter();
        else
            ItemsView.AttachFilter(e => FileTypes.Any(fileTypeFilter =>
                fileTypeFilter.FileType == e.FileType && fileTypeFilter.IsChecked));
    }

    public bool IsIndexed(string path)
    {
        return IndexCollection.ContainsKey(path);
    }

    public void AddToIndex(string path)
    {
        if (IsIndexed(path)) return;
        var item = new SearchViewItem
        {
            ItemDisplayName = Path.GetFileName(path),
            FileType = FileType.文件,
            OnlyKey = path,
            IconSymbol = 0xE7C3, // Default file icon symbol
            IsVisible = true
        };
        if (IndexCollection.TryAdd(path, item))
        {
            Dispatcher.UIThread.InvokeAsync(() => Items.Add(item));
        }
    }

    public void RemoveFromIndex(string path)
    {
        if (IndexCollection.TryRemove(path, out var item))
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                Items.Remove(item);
                PinnedItems.Remove(item);
            });
        }
    }

    public bool IsPinned(string path)
    {
        return PinnedItems.Any(e => e.OnlyKey == path);
    }

    public void SetPinned(string path, bool pinned)
    {
        if (IndexCollection.TryGetValue(path, out var item))
        {
            if (item.IsPined != pinned)
            {
                Pin(item);
            }
        }
    }
}
