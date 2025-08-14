#region

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using AvaloniaEdit.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Core.Services;
using Core.Services.Config;
using Core.Services.Plugin;
using Core.Utils;
using Pinyin.NET;
using PluginCore;
using PluginCore.SearchWindow.InputData;
using PluginCore.SearchWindow.InputDataAnalyzer;
using Serilog;

#endregion

namespace Core.ViewModel.Windows;

public class FileTypeFilter
{
    public FileType FileType { get; set; }
    public bool IsChecked { get; set; }
}

public partial class SearchWindowViewModel : ObservableRecipient
{
    private static ILogger Log = LogManager.Logger.ForContext<SearchWindowViewModel>();
    private static readonly List<SearchViewItem> TempList = new(1000);


    [ObservableProperty] private ObservableCollection<FileTypeFilter> _fileTypes = new();

    [ObservableProperty] private bool showFileTypeFilter = false;
    public readonly ConcurrentDictionary<string, SearchViewItem> _collection = new(); //存储本机所有软件
    private readonly TaskScheduler _scheduler = TaskScheduler.FromCurrentSynchronizationContext();

    private readonly DelayAction _searchDelayAction = new();

    [ObservableProperty] private bool? _everythingIsOk = true;
    [ObservableProperty] private ObservableCollection<SearchViewItem> _items = new(TempList);
    [ObservableProperty] private ObservableCollection<SearchViewItem> _showItems; //搜索界面显示的软件
    [ObservableProperty] public bool showInputData;

    [ObservableProperty] private ObservableCollection<InputData> _inputDatas = new();
    private PinyinSearcher<SearchViewItem> _pinyinSearcher;

    private bool _reloading = false;


    [ObservableProperty] private string? _search;


    [ObservableProperty] private int? _selectedIndex = -1;


    [ObservableProperty] private bool nowInSelectMode = false;
    private Action<SearchViewItem?>? selectAction;

    public SearchWindowViewModel()
    {
        Task.Run(() =>
        {
            ReloadApps(false);
            LoadLast();
        }).ContinueWith(e =>
        {
            if (e.Exception is not null) Log.Error(e.Exception, "");
        });
    }


    public void ReloadApps(bool logging = false)
    {
        if (_reloading) return;


        _reloading = true;
        CheckEverything();
        ServiceManager.Services.GetService<IAppToolService>()!.DelNullFile(_collection);
        ServiceManager.Services.GetService<IAppToolService>()!.GetAllApps(_collection, logging,
            ConfigManger.Config.useEverything);


        _reloading = false;
    }

    private void CheckEverything()
    {
        if (ConfigManger.Config.useEverything && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Log.Debug("everything检测");


            var service = ServiceManager.Services.GetService<IEverythingService>()!;
            EverythingIsOk = service.IsRun();

            if (!EverythingIsOk.Value)
                ServiceManager.Services.GetService<IAppToolService>()!.AutoStartEverything(_collection, () =>
                {
                    Thread.Sleep(1500);
                    var everythingService = ServiceManager.Services.GetService<IEverythingService>()!;
                    EverythingIsOk = everythingService.IsRun();
                });
        }
    }


    public void LoadLast()
    {
        if (!string.IsNullOrEmpty(Search)) return;


        Log.Debug("加载历史记录");


        foreach (var searchViewItem in Items) searchViewItem.Dispose();

        Items.Clear();

        var limit = 0;
        //Items.RaiseListChangedEvents = false;
        if (ConfigManger.Config.alwayShows.Any())
        {
            Log.Debug("加载常驻");
            foreach (var configAlwayShow in ConfigManger.Config.alwayShows)
                if (_collection.TryGetValue(configAlwayShow, out var searchViewItem))
                {
                    var item = (SearchViewItem)searchViewItem;

                    Log.Debug("加载常驻:" + item.OnlyKey);


                    item.IsPined = true;
                    Items.Add(item);


                    limit++;
                }
        }

        if (ConfigManger.Config.lastOpens.Any())
        {
            Log.Debug("加载历史");
            var sortedDict = ConfigManger.Config.lastOpens.OrderByDescending(p => p.Value)
                .ToDictionary(p => p.Key, p => p.Value);
            foreach (var (key, value) in sortedDict)
            {
                if (limit >= ConfigManger.Config.maxHistory)
                {
                    Log.Debug("超过历史记录限制,当前" + limit);


                    break;
                }

                if (_collection.TryGetValue(key, out var item2))
                {
                    if (item2 is null) break;

                    var item = (SearchViewItem)item2;

                    Log.Debug("加载历史:" + item.OnlyKey);


                    if (!Enumerable.Any<SearchViewItem>(Items, (e) => e.OnlyKey.Equals(item.OnlyKey)))
                    {
                        Items.Add(item);


                        limit++;
                    }
                }
            }
        }

        foreach (var searchViewItem in Items)
        {
            if (searchViewItem.PinyinItem is null) continue;

            searchViewItem.PinyinItem.CharMatchResults = [];
        }

        ShowItems = Items;
        FileTypes.Clear();
        ShowFileTypeFilter = FileTypes.Count > 0;
    }


    public void ProcessInputData(string? value, IInputDataAnalyzeTimeFlags nowTimeFlags)
    {
        if (Enumerable.LastOrDefault<SearchViewItem>(Items, e => e.FileType == FileType.自定义) is { } last)
        {
            var indexOf = Items.IndexOf(last);
            if (indexOf >= 0)
                for (var i = indexOf; i >= 0; i--)
                {
                    Items[i].Dispose();
                    Items.RemoveAt(i);
                }
        }

        InputDatas.Clear();
        foreach (var (key, funcs) in PluginOverall.SearchWindowInputDataIdentifies)
        foreach (var func in funcs)
        {
            var inputData = func.Invoke(nowTimeFlags, value);
            if (inputData != null) ExtensionMethods.AddRange(InputDatas, inputData);
        }

        if (!string.IsNullOrWhiteSpace(value))
            InputDatas.Add(new InputData
            {
                InputType = InputType.文本,
                Data = value
            });

        ShowInputData = InputDatas.Count > 0;
        foreach (var (key, funcs) in PluginOverall.SearchWindowInputDataAnalyzers)
        foreach (var func in funcs)
        {
            var inputDataAnalyzeTimeFlags = func.Item1.Invoke();
            if ((inputDataAnalyzeTimeFlags & nowTimeFlags) == 0) continue; // 如果当前时间标志不匹配，则跳过
            var enumerable = func.Item2.Invoke(InputDatas);
            foreach (var searchViewItem in enumerable) Items.Insert(0, searchViewItem);
        }
    }

    // ReSharper disable once RedundantAssignment
    partial void OnSearchChanged(string? value)
    {
        if (_pinyinSearcher is null)
            _pinyinSearcher = new PinyinSearcher<SearchViewItem>(_collection,
                nameof(SearchViewItem.PinyinItem), true);

        //Log.Debug("搜索");
        _searchDelayAction.Debounce(ConfigManger.Config.inputSmoothingMilliseconds, _scheduler, () =>
        {
            //Log.Debug("搜索开始");
            if (string.IsNullOrEmpty(Search))
            {
                LoadLast();
                ProcessInputData(null, IInputDataAnalyzeTimeFlags.搜索前);
                return;
            }

            Log.Debug("搜索变更:" + Search);
            // Items.RaiseListChangedEvents = false;

            #region 清除上次搜索结果

            foreach (var searchViewItem in Items) searchViewItem.Dispose();

            Items.Clear();

            if (Search is null) return;

            #endregion

            ProcessInputData(value, IInputDataAnalyzeTimeFlags.搜索时);

            var originalValue = Search;
            var lowerOriginalValue = Search.ToLowerInvariant();
            value = Enumerable.First<string>(Search.Split(" ")).ToLowerInvariant();
            var pluginItem = 0;


            if (originalValue.StartsWith(ConfigManger.Config.everythingSearchPreString) &&
                originalValue.Length > ConfigManger.Config.everythingSearchPreString.Length)
            {
                var useEverythingSearch = ServiceManager.Services.GetService<IAppToolService>()
                    .UseEverythingSearch(originalValue.Remove(0, ConfigManger.Config.everythingSearchPreString.Length),
                        ConfigManger.Config.everythingSearchMaxCount);
                ExtensionMethods.AddRange(Items, useEverythingSearch);
            }
            else
            {
                #region 从文件索引检索并排序

                var filtered = _pinyinSearcher.Search(value)
                    .ToList();

                var sorted = filtered.OrderByDescending(x => x.Weight)
                    .ToList();

                #endregion


                var count = 0; // 计数器变量
                const int limit = 100; // 限制次数
                Dictionary<SearchViewItem, int> nowHasLastOpens = new();

                for (var i = sorted.Count - 1; i >= 0; i--)
                    if (ConfigManger.Config.lastOpens.TryGetValue(sorted[i].Source.OnlyKey, out var open))
                    {
                        nowHasLastOpens.Add((SearchViewItem)sorted[i].Source, (int)sorted[i].Weight);
                        sorted.RemoveAt(i);
                    }

                var sortedDict = nowHasLastOpens.OrderByDescending(p => p.Value)
                    .ToDictionary(p => p.Key, p => p.Value);
                foreach (var (searchViewItem, i) in sortedDict)
                {
                    //Log.Debug("添加搜索结果" + searchViewItem.OnlyKey);
                    if (ConfigManger.Config.alwayShows.Contains(searchViewItem.OnlyKey)) searchViewItem.IsPined = true;


                    count++;
                    Items.Add(searchViewItem); // 添加元素
                }


                foreach (var x in sorted)
                {
                    if (count >= limit) // 如果达到了限制
                        break; // 跳出循环

                    var searchViewItem = (SearchViewItem)x.Source;
                    {
                        //Log.Debug("添加搜索结果" + x.Item.OnlyKey);


                        if (ConfigManger.Config.alwayShows.Contains(searchViewItem.OnlyKey))
                            searchViewItem.IsPined = true;

                        Items.Add(searchViewItem); // 添加元素
                        searchViewItem.Notify();
                        count++; // 计数器加一
                    }
                }

                //Items.RaiseListChangedEvents = true;
                var strings = Search.Split(" ", StringSplitOptions.RemoveEmptyEntries);
                if (strings.Length > 1)
                    for (var index = 1; index < strings.Length; index++)
                        ReSearch(strings[index]);
            }


            if (Items.Count <= pluginItem)
            {
                {
                    Log.Debug("无搜索项目,添加网页搜索");
                    var searchViewItem3 = new SearchViewItem
                    {
                        ItemDisplayName = "将内容添加至便签" + originalValue,
                        FileType = FileType.便签,
                        OnlyKey = originalValue,
                        Icon = null,
                        IconSymbol = 0xF6EC,
                        IsVisible = true
                    };
                    Items.Add(searchViewItem3);
                    var searchViewItem = new SearchViewItem
                    {
                        ItemDisplayName = "在网页中搜索" + originalValue,
                        FileType = FileType.URL,
                        OnlyKey = "https://www.bing.com/search?q=" + originalValue,
                        Icon = null,
                        IconSymbol = 62555,
                        IsVisible = true
                    };
                    Items.Add(searchViewItem);
                }
            }


            FileTypes.Clear();
            var fileTypes = Enumerable.GroupBy<SearchViewItem, FileType>(Items, e => e.FileType).Select(e => e.Key);
            foreach (var fileType in fileTypes)
                FileTypes.Add(new FileTypeFilter
                {
                    FileType = fileType,
                    IsChecked = false
                });
            ShowItems = Items;
            ShowFileTypeFilter = FileTypes.Count > 0;
        });
    }

    private PinyinSearcher<SearchViewItem>? _pinyinReSearcher;

    private void ReSearch(string value)
    {
        if (_pinyinReSearcher is null)
            _pinyinReSearcher = new PinyinSearcher<SearchViewItem>(Items,
                nameof(SearchViewItem.PinyinItem), false);

        var searchResultsEnumerable = _pinyinReSearcher.Search(value)
            .OrderByDescending(x => x.Weight)
            .ToList();
        Items.Clear();
        foreach (var searchResults in searchResultsEnumerable)
            Items.Add(searchResults.Source);
    }

    public void SetSelectMode(bool flag, Action<SearchViewItem> action)
    {
        NowInSelectMode = flag;
        selectAction = action;
    }

    [RelayCommand]
    public void OpenFile(SearchViewItem? item)
    {
        var s = Search;
        Task.Run(() =>
        {
            if (NowInSelectMode)
            {
                selectAction.Invoke(item);
                NowInSelectMode = false;
                WeakReferenceMessenger.Default.Send("a", "SearchWindowClose");
                return;
            }

            ServiceManager.Services.GetService<ISearchItemTool>()!.OpenFile(item, s);
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
    }

    [RelayCommand]
    private void RunAsAdmin(object searchViewItem)
    {
        Search = "";
        ServiceManager.Services.GetService<ISearchItemTool>()!.RunAsAdmin((SearchViewItem?)searchViewItem);
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
        Log.Debug("添加常驻" + item.OnlyKey);
        //Items.ResetItem(index);

        ServiceManager.Services.GetService<ISearchItemTool>()!.Pin(item);
    }

    [RelayCommand]
    private void OpenFolderInTerminal(object searchViewItem)
    {
        Search = "";
        ServiceManager.Services.GetService<ISearchItemTool>()!.OpenFolderInTerminal((SearchViewItem?)searchViewItem);
    }


    public void UpdateFilter()
    {
        if (Enumerable.All<FileTypeFilter>(FileTypes, e => !e.IsChecked))
            ShowItems = Items;
        else
            ShowItems = new ObservableCollection<SearchViewItem>(Enumerable.Where<SearchViewItem>(Items, e =>
                Enumerable.Any<FileTypeFilter>(FileTypes, e1 => e1.FileType == e.FileType && e1.IsChecked)));
    }
}