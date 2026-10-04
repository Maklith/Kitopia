using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Desktop.Abstractions.Shell;
using Kitopia.Desktop.Features.CustomScenario;
using Kitopia.Desktop.Features.CustomScenario.Services;
using Kitopia.Desktop.Features.CustomScenario.ViewModels;
using Kitopia.Desktop.Features.CustomScenario.Views;
using Kitopia.Desktop.Features.Services.Account;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Features.Utils;
using Kitopia.Desktop.Features.UI.UiControls.Plugin;
using Kitopia.Desktop.Features.ViewModel.Pages.plugin;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Ursa.Controls;
using PluginInfoUiHelper = Kitopia.Desktop.Features.Services.Plugin.PluginInfoUiHelper;

namespace Kitopia.Desktop.Features.ViewModel.Pages;

public sealed record PlatformOption(string Label, string Value);

public partial class MarketPageViewModel : ObservableObject
{
    private const int DefaultPageSize = 12;

    [ObservableProperty] private ObservableCollection<PluginInfoUiHelper> _plugins = new();
    [ObservableProperty] private int _currentPage = 1;
    [ObservableProperty] private int _totalPages = 1;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _pageSize = DefaultPageSize;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _keyword = string.Empty;
    [ObservableProperty] private PlatformOption _selectedPlatform;
    [ObservableProperty] private string _targetPageText = string.Empty;
    [ObservableProperty] private ObservableCollection<ScenarioMarketItem> _scenarios = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPluginMarket), nameof(IsScenarioMarket), nameof(IsMyScenarioMarket),
        nameof(ItemCountText))]
    private int _selectedMarketTab;
    [ObservableProperty] private string? _loadError;

    private int _loadGeneration;
    private CancellationTokenSource? _searchCts;
    private readonly IAccountService? _accountService;

    public IReadOnlyList<PlatformOption> PlatformOptions { get; } =
    [
        new("全部平台", ""),
        new("Windows", "windows"),
        new("macOS", "macos"),
        new("Linux", "linux")
    ];

    public bool CanPreviousPage => CurrentPage > 1;
    public bool CanNextPage => CurrentPage < TotalPages;
    public bool HasMultiplePages => TotalPages > 1;
    public string PageDisplayText => $"{CurrentPage} / {TotalPages}";
    public bool HasNoPlugins => !IsLoading && Plugins.Count == 0;
    public bool HasNoScenarios => !IsLoading && Scenarios.Count == 0;
    public bool IsPluginMarket => SelectedMarketTab == 0;
    public bool IsScenarioMarket => SelectedMarketTab != 0;
    public bool IsMyScenarioMarket => SelectedMarketTab == 2;
    public IReadOnlyList<string> MarketTabs { get; } = ["插件", "情景", "我的情景"];
    public string ItemCountText => $"共 {TotalCount} 个{(IsPluginMarket ? "插件" : "情景")}";

    partial void OnSelectedMarketTabChanged(int value)
    {
        if (CurrentPage == 1) _ = LoadPluginsAsync();
        else CurrentPage = 1;
    }

    partial void OnTotalCountChanged(int value) => OnPropertyChanged(nameof(ItemCountText));

    public MarketPageViewModel() : this(null)
    {
    }

    public MarketPageViewModel(IAccountService? accountService)
    {
        _accountService = accountService;
        if (_accountService != null)
        {
            _accountService.UserStateChanged += OnUserStateChanged;
        }

        _selectedPlatform = PlatformOptions[0];
        _ = LoadPluginsAsync();
    }

    private void OnUserStateChanged(UserInfo? user)
    {
        if (Avalonia.Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            CurrentPage = 1;
            _ = LoadPluginsAsync();
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            CurrentPage = 1;
            _ = LoadPluginsAsync();
        });
    }

    ~MarketPageViewModel()
    {
        if (_accountService != null)
        {
            _accountService.UserStateChanged -= OnUserStateChanged;
        }

        for (var i = 0; i < _plugins.Count; i++) _plugins[i].Icon?.Dispose();
    }

    partial void OnKeywordChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, token);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested) return;
                    CurrentPage = 1;
                    _ = LoadPluginsAsync();
                });
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    partial void OnSelectedPlatformChanged(PlatformOption value)
    {
        CurrentPage = 1;
        _ = LoadPluginsAsync();
    }

    partial void OnCurrentPageChanged(int value)
    {
        OnPropertyChanged(nameof(CanPreviousPage));
        OnPropertyChanged(nameof(CanNextPage));
        OnPropertyChanged(nameof(PageDisplayText));
        _ = LoadPluginsAsync();
    }

    partial void OnTotalPagesChanged(int value)
    {
        OnPropertyChanged(nameof(CanPreviousPage));
        OnPropertyChanged(nameof(CanNextPage));
        OnPropertyChanged(nameof(HasMultiplePages));
        OnPropertyChanged(nameof(PageDisplayText));
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CanPreviousPage)
        {
            CurrentPage--;
        }
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CanNextPage)
        {
            CurrentPage++;
        }
    }

    [RelayCommand]
    private void JumpToPage()
    {
        if (int.TryParse(TargetPageText?.Trim(), out var target) && target >= 1 && target <= TotalPages)
        {
            TargetPageText = string.Empty;
            CurrentPage = target;
        }
        else
        {
            TargetPageText = string.Empty;
        }
    }

    [RelayCommand]
    private void Search()
    {
        CurrentPage = 1;
        _ = LoadPluginsAsync();
    }

    private async Task LoadPluginsAsync()
    {
        var generation = ++_loadGeneration;
        IsLoading = true;
        LoadError = null;
        OnPropertyChanged(nameof(HasNoPlugins));
        OnPropertyChanged(nameof(HasNoScenarios));
        try
        {
            if (IsScenarioMarket)
            {
                var scenarios = await ScenarioMarketService.GetScenariosAsync(
                    CurrentPage, PageSize, Keyword, SelectedMarketTab == 2);
                if (generation != _loadGeneration) return;
                Scenarios.Clear();
                foreach (var item in scenarios.Items) Scenarios.Add(item);
                TotalCount = scenarios.TotalCount;
                TotalPages = Math.Max(scenarios.TotalPages, 1);
                return;
            }
            var page = await PluginNetworkService.GetPluginsAsync(
                CurrentPage,
                PageSize,
                Keyword,
                SelectedPlatform?.Value);

            if (generation != _loadGeneration || page is null)
            {
                return;
            }

            foreach (var plugin in Plugins)
            {
                plugin.Dispose();
            }

            Plugins.Clear();
            foreach (var plugin in page.Items)
            {
                Plugins.Add(new PluginInfoUiHelper
                {
                    PluginBaseInfo = plugin.ToPluginBaseInfo(),
                    OnlinePluginInfo = plugin,
                    IsLocal = false,
                    AuthorName = !string.IsNullOrWhiteSpace(plugin.AuthorNickname)
                        ? plugin.AuthorNickname
                        : !string.IsNullOrWhiteSpace(plugin.AuthorUserName)
                            ? plugin.AuthorUserName
                            : null
                });
            }

            TotalCount = page.TotalCount;
            TotalPages = Math.Max(page.TotalPages, 1);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException)
        {
            if (generation == _loadGeneration)
            {
                LoadError = exception.Message;
                Scenarios.Clear();
                TotalCount = 0;
                TotalPages = 1;
            }
        }
        finally
        {
            if (generation == _loadGeneration)
            {
                IsLoading = false;
                OnPropertyChanged(nameof(HasNoPlugins));
                OnPropertyChanged(nameof(HasNoScenarios));
            }
        }
    }

    [RelayCommand]
    private async Task ShowScenarioDetail(ScenarioMarketItem scenario)
    {
        var viewModel = new ScenarioDetailViewModel(scenario);
        _ = viewModel.InitializeAsync();
        await OverlayDialog.ShowCustomModal<ScenarioDetail, ScenarioDetailViewModel, object>(viewModel, "LocalHost",
            new OverlayDialogOptions
            {
                CanLightDismiss = true,
                CanDragMove = false,
                IsCloseButtonVisible = false
            });
    }

    [RelayCommand]
    private async Task UploadScenarioVersion(ScenarioMarketItem scenario)
    {
        var local = CustomScenarioManger.CustomScenarios.FirstOrDefault(item =>
            string.Equals(item.Uuid, scenario.SourceUuid, StringComparison.OrdinalIgnoreCase));
        if (local is null)
        {
            await ShowToastAsync("无法发布版本", "本地找不到对应的情景文件，请先在客户端打开该情景。", NotificationType.Warning);
            return;
        }

        await ServiceManager.Services.GetRequiredService<IScenarioUploadService>().ShowAsync(
            local, ServiceManager.Services.GetService<IWindowTool>()?.GetForegroundWindow());
        _ = LoadPluginsAsync();
    }

    [RelayCommand]
    private async Task EditScenarioInformation(ScenarioMarketItem scenario)
    {
        await ServiceManager.Services.GetRequiredService<IScenarioUploadService>().ShowInformationAsync(
            scenario, ServiceManager.Services.GetService<IWindowTool>()?.GetForegroundWindow());
        _ = LoadPluginsAsync();
    }

    [RelayCommand]
    private async Task CancelScenarioReview(ScenarioMarketItem scenario)
    {
        if (scenario.Review is not { Status: 0 } review) return;
        try
        {
            await ScenarioMarketService.CancelReviewAsync(scenario.Id, review.Id);
            await LoadPluginsAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException)
        {
            await ShowToastAsync("取消审核失败", exception.Message, NotificationType.Error);
        }
    }

    [RelayCommand]
    private void WithdrawScenarioRelease(ScenarioMarketItem scenario)
    {
        if (string.IsNullOrWhiteSpace(scenario.LastVersion)) return;
        ServiceManager.Services.GetRequiredService<IToastService>().Show(new ToastRequest
        {
            Header = "撤回情景版本",
            Text = $"确定撤回 v{scenario.LastVersion} 吗？撤回后该版本将不能再次发布。",
            AutoCloseDelay = null,
            Actions =
            [
                new ToastAction
                {
                    Text = "撤回", IsPrimary = true,
                    Callback = () => _ = WithdrawScenarioReleaseAsync(scenario)
                },
                new ToastAction { Text = "取消" }
            ]
        }, ServiceManager.Services.GetService<IWindowTool>()?.GetForegroundWindow());
    }

    private async Task WithdrawScenarioReleaseAsync(ScenarioMarketItem scenario)
    {
        try
        {
            await ScenarioMarketService.WithdrawReleaseAsync(scenario.Id, scenario.LastVersion!);
            await LoadPluginsAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException)
        {
            await ShowToastAsync("撤回失败", exception.Message, NotificationType.Error);
        }
    }

    [RelayCommand]
    private void DeleteScenario(ScenarioMarketItem scenario)
    {
        ServiceManager.Services.GetRequiredService<IToastService>().Show(new ToastRequest
        {
            Header = "删除情景",
            Text = $"确定删除“{scenario.Name}”吗？删除后不能恢复。",
            AutoCloseDelay = null,
            Actions =
            [
                new ToastAction
                {
                    Text = "删除", IsPrimary = true,
                    Callback = () => _ = DeleteScenarioAsync(scenario)
                },
                new ToastAction { Text = "取消" }
            ]
        }, ServiceManager.Services.GetService<IWindowTool>()?.GetForegroundWindow());
    }

    private async Task DeleteScenarioAsync(ScenarioMarketItem scenario)
    {
        try
        {
            await ScenarioMarketService.DeleteAsync(scenario.Id);
            await LoadPluginsAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or JsonException)
        {
            await ShowToastAsync("删除失败", exception.Message, NotificationType.Error);
        }
    }

    [RelayCommand]
    private void ManageScenarios()
    {
        ServiceManager.Services.GetRequiredService<IDesktopShell>().Open($"{ConfigManger.WebUrl}/developer/scenarios");
    }

    [RelayCommand]
    private async Task DownloadPlugin(OnlinePluginInfo plugin)
    {
        var versions = await PluginNetworkService.GetAvailableVersionsAsync(plugin.NameSign);
        if (versions is null)
        {
            await ShowToastAsync("无法获取可下载版本", $"未能获取插件 {plugin.Name} 的版本信息。", NotificationType.Warning);
            return;
        }

        var versionOptions = versions
            .Where(version => !string.IsNullOrWhiteSpace(version.Version) &&
                              PluginNetworkService.SupportsCurrentPlatform(version.AvailablePlatforms))
            .Select(version => version.Version)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (versionOptions.Count == 0)
        {
            await ShowToastAsync("无法获取可下载版本", $"未能获取插件 {plugin.Name} 的版本信息。", NotificationType.Warning);
            return;
        }

        var request = new ToastRequest
        {
            Header = $"下载 {plugin.Name}",
            Text = "请选择要下载的版本。下载时会校验当前系统是否支持所选版本。",
            NotificationType = NotificationType.Information,
            AutoCloseDelay = null,
            ShowCloseButton = true,
            SelectionOptions = versionOptions,
            SelectedOption = versionOptions[0],
            SelectionConfirmText = "确定",
            SelectionConfirmed = version => _ = DownloadSelectedVersionAsync(plugin, version)
        };

        await ServiceManager.Services.GetRequiredService<IToastService>().Show(
            request,
            ServiceManager.Services.GetService<IWindowTool>()?.GetForegroundWindow());
    }

    private static async Task DownloadSelectedVersionAsync(OnlinePluginInfo plugin, string version)
    {
        var downloaded = await PluginManager.DownloadPluginAndEnable(plugin.NameSign, version);
        if (downloaded)
        {
            await ShowToastAsync("插件已安装", $"{plugin.Name} {version} 已下载并启用。", NotificationType.Success);
            return;
        }

        await ShowToastAsync(
            "插件下载失败",
            $"无法下载 {plugin.Name} {version}。该版本可能不支持当前系统，请选择其他版本。",
            NotificationType.Warning);
    }

    private static Task ShowToastAsync(string header, string text, NotificationType notificationType)
    {
        return ServiceManager.Services.GetRequiredService<IToastService>().Show(
            header,
            text,
            notificationType,
            ServiceManager.Services.GetService<IWindowTool>()?.GetForegroundWindow());
    }

    [RelayCommand]
    private async Task ShowPluginDetail(PluginInfoUiHelper pluginInfoUiHelper)
    {
        var overlayDialogOptions = new OverlayDialogOptions
        {
            CanLightDismiss = true,
            CanDragMove = false,
            IsCloseButtonVisible = false,
        };
        await OverlayDialog.ShowCustomModal<PluginDetail, PluginDetailViewModel, object>(
            new PluginDetailViewModel(pluginInfoUiHelper, SearchAuthorByName), "LocalHost", overlayDialogOptions);
    }

    [RelayCommand]
    public void SearchAuthor(PluginInfoUiHelper? plugin)
    {
        if (plugin is null) return;
        var authorIdentifier = !string.IsNullOrWhiteSpace(plugin.OnlinePluginInfo?.AuthorUserName)
            ? plugin.OnlinePluginInfo.AuthorUserName
            : plugin.AuthorName;

        SearchAuthorByName(authorIdentifier);
    }

    public void SearchAuthorByName(string? author)
    {
        if (string.IsNullOrWhiteSpace(author)) return;
        Keyword = $"@{author.TrimStart('@')}";
        CurrentPage = 1;
        _ = LoadPluginsAsync();
    }

    [RelayCommand]
    public void SearchTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return;
        Keyword = tag.Trim();
        CurrentPage = 1;
        _ = LoadPluginsAsync();
    }
}
