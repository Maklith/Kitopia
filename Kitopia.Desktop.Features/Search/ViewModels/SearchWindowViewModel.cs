#region

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Features.Search.InputProcessing;
using Kitopia.Desktop.Features.Search.Semantic;
using Microsoft.Extensions.DependencyInjection;
using ObservableCollections;
using PluginCore;
using PluginCore.SearchWindow.InputData;
using PluginCore.SearchWindow.InputDataAnalyzer;
using ReactiveUI;
using Serilog;

#endregion

namespace Kitopia.Desktop.Features.Search.ViewModels;

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
public partial class SearchWindowViewModel : ObservableRecipient, ISearchFeatureService
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<SearchWindowViewModel>();
    public readonly SearchIndex Index = new();

    [ObservableProperty] private bool? _everythingIsOk = true;


    [ObservableProperty] private ObservableCollection<FileTypeFilter> _fileTypes = new();

    [ObservableProperty] private ObservableCollection<InputData> _inputDatas = new();
    [ObservableProperty] private ObservableList<SearchViewItem> _items = new(100);
    [ObservableProperty] private ObservableCollection<SearchViewItem> _pinnedItems = new();
    [ObservableProperty] private ISynchronizedView<SearchViewItem, SearchViewItem> _itemsView;
    [ObservableProperty] private NotifyCollectionChangedSynchronizedViewList<SearchViewItem> _itemsViewList;
    [ObservableProperty] private SearchViewItem? _selectedItem;
    [ObservableProperty] private bool _isPreviewMode;
    [ObservableProperty] private bool _canUsePreview;
    [ObservableProperty] private bool? _previewModeOverride;
    [ObservableProperty] private string? _previewContent;
    [ObservableProperty] private string? _previewLocation;
    [ObservableProperty] private bool _isPreviewImage;


    [ObservableProperty] private bool _nowInSelectMode;

    private bool _reloading;
    private int _loadLastRequestId;
    private int _loadLastAppliedId;
    private int _loadLastScheduled;
    private int _searchVersion;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _previewCancellation;
    private int _previewVersion;
    private readonly Dictionary<string, SearchResultContext> _resultContexts = new(StringComparer.Ordinal);


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
        BuiltInSearchInputs.EnsureRegistered();
        ItemsView = Items.CreateView(e => e);
        ItemsViewList = ItemsView.ToNotifyCollectionChanged();
        Task.Run(() =>
        {
            ReloadApps();
        }).ContinueWith(e =>
        {
            if (e.Exception is not null) Logger.Error(e.Exception, "");
        });
        this.WhenAnyValue(e => e.Search)
            .Throttle(TimeSpan.FromMilliseconds(Math.Max(100, ConfigManger.Config.semanticSearchDebounceMilliseconds)))
            .DistinctUntilChanged()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(ToSearch, e => { Logger.Error(e, ""); });
    }

    partial void OnSelectedItemChanged(SearchViewItem? value)
    {
        UpdatePreview(value);
    }

    partial void OnNowInSelectModeChanged(bool value)
    {
        if (value)
        {
            PreviewModeOverride = null;
        }

        UpdateDisplayMode();
    }

    [RelayCommand]
    private void TogglePreviewMode()
    {
        if (!CanUsePreview)
        {
            return;
        }

        PreviewModeOverride = IsPreviewMode ? false : true;
        UpdateDisplayMode();
    }

    public void ClosePreviewMode()
    {
        PreviewModeOverride = false;
        UpdateDisplayMode();
    }


    private readonly Dictionary<object, List<string>> _analyzerIndexedKeys = new();

    public void SetEverythingAvailability(bool? isAvailable)
    {
        RunOnUiThread(() => EverythingIsOk = isAvailable);
    }

    public void AddPluginItems(IEnumerable<SearchViewItem> items)
    {
        foreach (var item in items)
        {
            Index.TryAdd(ToSearchEntry(item));
        }

        Index.RebuildSearcher();
    }

    public void RemovePluginItems(IEnumerable<SearchViewItem> items)
    {
        foreach (var item in items)
        {
            Index.TryRemove(item.OnlyKey);
            RunOnUiThread(() =>
            {
                var indexedItem = Items.FirstOrDefault(candidate => candidate.OnlyKey == item.OnlyKey);
                if (indexedItem is not null)
                {
                    Items.Remove(indexedItem);
                }

                var pinnedItem = PinnedItems.FirstOrDefault(candidate => candidate.OnlyKey == item.OnlyKey);
                if (pinnedItem is not null)
                {
                    PinnedItems.Remove(pinnedItem);
                }
            });
        }

        Index.RebuildSearcher();
    }

    private static SearchEntry ToSearchEntry(SearchViewItem item)
    {
        return new SearchEntry
        {
            DisplayName = item.ItemDisplayName,
            OnlyKey = item.OnlyKey,
            FileType = item.FileType,
            IconSymbol = item.IconSymbol,
            Arguments = item.Arguments,
            LaunchPath = item.LaunchPath,
            IconPath = item.IconPath,
            StartDirectory = item.StartDirectory
        };
    }
    
    public void RemoveAnalyzerIndex(object analyzer)
    {
        if (_analyzerIndexedKeys.TryGetValue(analyzer, out var keys))
        {
            foreach (var key in keys)
            {
                if (Index.TryRemove(key))
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var svItem = Items.FirstOrDefault(e => e.OnlyKey == key);
                        if (svItem is not null) Items.Remove(svItem);
                        svItem = PinnedItems.FirstOrDefault(e => e.OnlyKey == key);
                        if (svItem is not null) PinnedItems.Remove(svItem);
                    });
                }
            }
            _analyzerIndexedKeys.Remove(analyzer);
            Index.RebuildSearcher();
        }
    }
    
    public void UpdateIndexOnWindowOpen()
    {
        var changed = false;
        var requiresRebuild = false;
        var appendedEntries = new List<SearchEntry>();
        foreach (var (_, analyzers) in PluginOverall.SearchWindowInputDataAnalyzers)
        foreach (var analyzerTuple in analyzers)
        {
            var timeFlags = analyzerTuple.Item1();
            if ((timeFlags & InputDataAnalyzeTimeFlags.WindowOpenUpdateIndex) != 0)
            {
                if (_analyzerIndexedKeys.TryGetValue(analyzerTuple, out var oldKeys))
                {
                    foreach (var key in oldKeys) Index.TryRemove(key);
                    if (oldKeys.Count > 0)
                    {
                        changed = true;
                        requiresRebuild = true;
                    }
                }

                var newItems = analyzerTuple.Item2(new List<InputData>()).ToList();
                var newKeys = new List<string>();

                foreach (var item in newItems)
                {
                    var entry = new SearchEntry
                    {
                        DisplayName = item.ItemDisplayName,
                        OnlyKey = item.OnlyKey,
                        FileType = item.FileType,
                        IconSymbol = item.IconSymbol,
                        Arguments = item.Arguments,
                        LaunchPath = item.LaunchPath,
                        IconPath = item.IconPath,
                        StartDirectory = item.StartDirectory
                    };
                    if (Index.TryAdd(entry))
                    {
                        newKeys.Add(item.OnlyKey);
                        appendedEntries.Add(entry);
                    }
                }

                _analyzerIndexedKeys[analyzerTuple] = newKeys;
                if (newKeys.Count > 0) changed = true;
            }
        }

        if (requiresRebuild)
            Index.RebuildSearcher();
        else if (changed)
            Index.AppendToSearcher(appendedEntries);
    }
    
    public void ReloadApps(bool logging = false)
    {
        if (_reloading) return;


        _reloading = true;
        CheckEverything();
        ServiceManager.Services.GetService<IAppToolService>()!.CleanupInvalidItems(Index);
        ServiceManager.Services.GetService<IAppToolService>()!.IndexAllApps(Index, logging,
            ConfigManger.Config.useEverything);
        Index.RebuildSearcher();

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
                ServiceManager.Services.GetService<IAppToolService>()!.AutoStartEverything(Index, () =>
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
        _resultContexts.Clear();
        PreviewModeOverride = null;
        IsPreviewMode = false;
        SelectedItem = null;

        var limit = 0;
        //Items.RaiseListChangedEvents = false;
        if (ConfigManger.Config.alwayShows.Any())
        {
            Logger.Debug("加载常驻");
            foreach (var configAlwayShow in ConfigManger.Config.alwayShows)
                if (Index.TryGetValue(configAlwayShow, out var searchEntry))
                {
                    var item = searchEntry.ToSearchViewItem();

                    Logger.Debug("加载常驻:" + item.OnlyKey);


                    item.IsPined = true;
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

                if (Index.TryGetValue(key, out var entry))
                {
                    var item = entry.ToSearchViewItem();
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
        UpdateDisplayMode();
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
        
        foreach (var inputData in inputDatas) InputDatas.Add(inputData);
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

    public void ToSearch(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            Interlocked.Exchange(ref _searchCancellation, null)?.Cancel();
            LoadLast();
            return;
        }
        ShowPinnedItems = false;

        Logger.Debug("搜索变更:" + value);

        Items.Clear();
        PinnedItems.Clear();
        _resultContexts.Clear();
        PreviewModeOverride = null;
        IsPreviewMode = false;
        SelectedItem = null;

        ProcessInputData(value, InputDataAnalyzeTimeFlags.InputChanged);

        var originalValue = value;
        value = value.ToLowerInvariant();
        var version = Interlocked.Increment(ref _searchVersion);
        if (originalValue.StartsWith(ConfigManger.Config.everythingSearchPreString) &&
            originalValue.Length > ConfigManger.Config.everythingSearchPreString.Length)
        {
            Interlocked.Exchange(ref _searchCancellation, null)?.Cancel();
            var useEverythingSearch = ServiceManager.Services.GetService<IAppToolService>()
                .SearchWithEverything(originalValue.Remove(0, ConfigManger.Config.everythingSearchPreString.Length),
                    ConfigManger.Config.everythingSearchMaxCount);
            Items.AddRange(useEverythingSearch);
            RefreshFileTypes();
            UpdateDisplayMode();
        }
        else
        {
            var cancellation = new CancellationTokenSource();
            Interlocked.Exchange(ref _searchCancellation, cancellation)?.Cancel();
            _ = Task.Run(async () =>
            {
                try
                {
                    await SearchInBackgroundAsync(value, originalValue, version, cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    // A newer query superseded this result.
                }
                catch (Exception exception)
                {
                    Logger.Error(exception, "Search failed");
                }
            });
        }
    }

    private async Task SearchInBackgroundAsync(
        string value,
        string originalValue,
        int version,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pinyinResults = Index.Search(value);
        cancellationToken.ThrowIfCancellationRequested();
        var pinyinItems = CreateSearchItems(pinyinResults
            .Select(result => new SearchIndexResult(result.Source, result.Weight, result.CharMatchResults))
            .ToList());
        if (pinyinItems.Count > 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (Volatile.Read(ref _searchVersion) != version) return;
                Items.AddRange(pinyinItems);
                RefreshFileTypes();
            });
        }

        var rawResults = await Index.SearchAsync(value, pinyinResults, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (rawResults.Count == 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (Volatile.Read(ref _searchVersion) != version) return;
                if (Items.Count <= 0)
                {
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
                    RefreshFileTypes();
                }

                UpdateDisplayMode();
            });
            return;
        }

        var resultsToAdd = CreateSearchItems(rawResults);
        var shouldReplacePinyinResults = !pinyinItems.Select(item => item.OnlyKey)
            .SequenceEqual(resultsToAdd.Select(item => item.OnlyKey), StringComparer.Ordinal);
        if (!shouldReplacePinyinResults)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (Volatile.Read(ref _searchVersion) != version) return;
                UpdateResultContexts(rawResults);
                UpdateDisplayMode();
            });
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (Volatile.Read(ref _searchVersion) != version) return;
            foreach (var item in pinyinItems)
            {
                Items.Remove(item);
            }
            Items.AddRange(resultsToAdd);
            UpdateResultContexts(rawResults);

            if (Items.Count <= 0)
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

            RefreshFileTypes();
            UpdateDisplayMode();
        });
    }

    private List<SearchViewItem> CreateSearchItems(IReadOnlyList<SearchIndexResult> results)
    {
        const int limit = 100;
        var resultItems = new List<SearchViewItem>(Math.Min(results.Count, limit));

        foreach (var result in results)
        {
            if (!ConfigManger.Config.lastOpens.ContainsKey(result.Source.OnlyKey)) continue;

            resultItems.Add(ToSearchViewItem(result));
            if (resultItems.Count >= limit) return resultItems;
        }

        foreach (var result in results)
        {
            if (ConfigManger.Config.lastOpens.ContainsKey(result.Source.OnlyKey)) continue;

            resultItems.Add(ToSearchViewItem(result));
            if (resultItems.Count >= limit) return resultItems;
        }

        return resultItems;
    }

    private static SearchViewItem ToSearchViewItem(SearchIndexResult result)
    {
        var item = result.Source.ToSearchViewItem();
        item.PinyinItem = result.CharMatchResults;
        if (ConfigManger.Config.alwayShows.Contains(item.OnlyKey)) item.IsPined = true;
        item.Notify();
        return item;
    }

    private void UpdateResultContexts(IEnumerable<SearchIndexResult> results)
    {
        _resultContexts.Clear();
        foreach (var result in results)
        {
            _resultContexts[result.Source.OnlyKey] = new SearchResultContext(result.SemanticContentChunkIndex);
        }
    }

    private void UpdateDisplayMode()
    {
        CanUsePreview = !NowInSelectMode
                        && !string.IsNullOrWhiteSpace(Search)
                        && Items.Any(SearchDisplayPolicy.IsPreviewCandidate);

        if (!CanUsePreview)
        {
            IsPreviewMode = false;
        }
        else if (PreviewModeOverride is not null)
        {
            IsPreviewMode = PreviewModeOverride.Value;
        }
        else
        {
            IsPreviewMode = SearchDisplayPolicy.ShouldUsePreview(NowInSelectMode, Search, Items, _resultContexts);
        }

        if (IsPreviewMode)
        {
            if (SelectedItem is null || !SearchDisplayPolicy.IsPreviewCandidate(SelectedItem))
            {
                SelectedItem = Items.FirstOrDefault(SearchDisplayPolicy.IsPreviewCandidate);
            }
            else
            {
                UpdatePreview(SelectedItem);
            }
        }
        else
        {
            PreviewContent = null;
            PreviewLocation = null;
            IsPreviewImage = false;
        }
    }

    private void UpdatePreview(SearchViewItem? item)
    {
        var version = Interlocked.Increment(ref _previewVersion);
        Interlocked.Exchange(ref _previewCancellation, null)?.Cancel();
        PreviewContent = null;
        PreviewLocation = null;
        IsPreviewImage = false;
        if (!IsPreviewMode || item is null || !SearchDisplayPolicy.IsPreviewCandidate(item))
        {
            return;
        }

        PreviewLocation = item.OnlyKey;
        IsPreviewImage = item.FileType == FileType.图像;
        if (IsPreviewImage)
        {
            return;
        }

        if (!DocumentTextExtractor.TryCreateSource(item.OnlyKey, out var source))
        {
            PreviewContent = "此文件暂不支持内嵌文本预览。";
            return;
        }

        PreviewContent = "正在载入内容...";
        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref _previewCancellation, cancellation)?.Cancel();
        var chunkIndex = _resultContexts.TryGetValue(item.OnlyKey, out var context)
            ? context.SemanticContentChunkIndex
            : null;
        _ = Task.Run(async () =>
        {
            try
            {
                var preview = await LoadPreviewContentAsync(source, chunkIndex, cancellation.Token);
                Dispatcher.UIThread.Post(() =>
                {
                    if (Volatile.Read(ref _previewVersion) != version || SelectedItem?.OnlyKey != item.OnlyKey) return;
                    PreviewContent = preview ?? "未能从此文件读取文本内容。";
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Logger.Debug(exception, "Could not load search preview");
                Dispatcher.UIThread.Post(() =>
                {
                    if (Volatile.Read(ref _previewVersion) != version || SelectedItem?.OnlyKey != item.OnlyKey) return;
                    PreviewContent = "当前无法读取此文件。";
                });
            }
        });
    }

    private static async Task<string?> LoadPreviewContentAsync(
        DocumentContentSource source,
        int? semanticChunkIndex,
        CancellationToken cancellationToken)
    {
        var targetChunkIndex = semanticChunkIndex ?? 0;
        var firstChunkIndex = Math.Max(0, targetChunkIndex - 1);
        var lastChunkIndex = targetChunkIndex + 1;
        var chunks = new List<string>();
        var index = 0;
        await foreach (var chunk in DocumentTextExtractor.ExtractChunksAsync(
                           source,
                           BgeOnnxEmbeddingService.CountDocumentTokens,
                           cancellationToken))
        {
            if (index >= firstChunkIndex && index <= lastChunkIndex)
            {
                chunks.Add(chunk);
            }

            if (index > lastChunkIndex)
            {
                break;
            }

            index++;
        }

        return chunks.Count == 0 ? null : string.Join(Environment.NewLine + Environment.NewLine, chunks);
    }

    public void ActivateItem(SearchViewItem? item)
    {
        if (item is not null && IsPreviewMode && SearchDisplayPolicy.IsPreviewCandidate(item))
        {
            SelectedItem = item;
            return;
        }

        OpenFile(item);
    }

    private void RefreshFileTypes()
    {
        FileTypes.Clear();
        foreach (var fileType in Items.Select(item => item.FileType).Distinct())
        {
            FileTypes.Add(new FileTypeFilter { FileType = fileType, IsChecked = false });
        }

        ShowFileTypeFilter = FileTypes.Count > 0;
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
        return Index.ContainsKey(path);
    }

    public void AddToIndex(string path)
    {
        if (IsIndexed(path)) return;
        var entry = new SearchEntry
        {
            DisplayName = Path.GetFileName(path),
            OnlyKey = path,
            FileType = FileType.文件,
            IconSymbol = 0xE7C3
        };
        if (Index.TryAddAndRefreshSearcher(entry))
        {
            Dispatcher.UIThread.InvokeAsync(() => Items.Add(entry.ToSearchViewItem()));
        }
    }

    public void RemoveFromIndex(string path)
    {
        if (Index.TryRemoveAndRefreshSearcher(path))
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var svItem = Items.FirstOrDefault(e => e.OnlyKey == path);
                if (svItem is not null) Items.Remove(svItem);
                svItem = PinnedItems.FirstOrDefault(e => e.OnlyKey == path);
                if (svItem is not null) PinnedItems.Remove(svItem);
            });
        }
    }

    public bool IsPinned(string path)
    {
        return SearchPinState.IsPinned(ConfigManger.Config.alwayShows, path);
    }

    public void SetPinned(string path, bool pinned)
    {
        var hasEntry = Index.TryGetValue(path, out var entry);
        if (pinned && !hasEntry) return;
        var configChanged = SearchPinState.SetPinned(ConfigManger.Config.alwayShows, path, pinned);

        if (configChanged) ConfigManger.Save();
        RunOnUiThread(() =>
        {
            var item = PinnedItems.FirstOrDefault(e => e.OnlyKey == path);
            if (pinned)
            {
                item ??= entry.ToSearchViewItem();
                item.IsPined = true;
                if (!PinnedItems.Contains(item)) PinnedItems.Insert(0, item);
            }
            else if (item is not null)
            {
                item.IsPined = false;
                PinnedItems.Remove(item);
            }

            var indexedItem = Items.FirstOrDefault(e => e.OnlyKey == path);
            if (indexedItem is not null) indexedItem.IsPined = pinned;

            if (string.IsNullOrEmpty(Search)) ShowPinnedItems = PinnedItems.Count > 0;
        });
    }
}
