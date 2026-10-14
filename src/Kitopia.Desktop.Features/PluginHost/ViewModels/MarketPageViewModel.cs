using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Feature.Localization;
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
        new(Lang.Get("lang.kitopia.all_platforms"), ""),
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
    public IReadOnlyList<string> MarketTabs { get; } = [Lang.Get("lang.kitopia.plugins"), Lang.Get("lang.kitopia.scenarios"), Lang.Get("lang.kitopia.my_scenarios")];
    public string ItemCountText => Lang.Format(IsPluginMarket ? "lang.kitopia.value_plugins" : "lang.kitopia.value_scenarios", TotalCount);

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
            await ShowToastAsync(Lang.Get("lang.kitopia.cannot_publish_version"), Lang.Get("lang.kitopia.scenario_file_not_found_open_the_scenario_in_the_client_first"), NotificationType.Warning);
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
            await ShowToastAsync(Lang.Get("lang.kitopia.failed_to_cancel_review"), exception.Message, NotificationType.Error);
        }
    }

    [RelayCommand]
    private void WithdrawScenarioRelease(ScenarioMarketItem scenario)
    {
        if (string.IsNullOrWhiteSpace(scenario.LastVersion)) return;
        ServiceManager.Services.GetRequiredService<IToastService>().Show(new ToastRequest
        {
            Header = Lang.Get("lang.kitopia.withdraw_scenario_version"),
            Text = Lang.Format("lang.kitopia.withdraw_v_value_this_version_cannot_be_published_again", scenario.LastVersion),
            AutoCloseDelay = null,
            Actions =
            [
                new ToastAction
                {
                    Text = Lang.Get("lang.kitopia.withdraw"), IsPrimary = true,
                    Callback = () => _ = WithdrawScenarioReleaseAsync(scenario)
                },
                new ToastAction { Text = Lang.Get("lang.kitopia.cancel") }
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
            await ShowToastAsync(Lang.Get("lang.kitopia.withdrawal_failed"), exception.Message, NotificationType.Error);
        }
    }

    [RelayCommand]
    private void DeleteScenario(ScenarioMarketItem scenario)
    {
        ServiceManager.Services.GetRequiredService<IToastService>().Show(new ToastRequest
        {
            Header = Lang.Get("lang.kitopia.delete_scenario"),
            Text = Lang.Format("lang.kitopia.delete_value_this_cannot_be_undone", scenario.Name),
            AutoCloseDelay = null,
            Actions =
            [
                new ToastAction
                {
                    Text = Lang.Get("lang.kitopia.delete"), IsPrimary = true,
                    Callback = () => _ = DeleteScenarioAsync(scenario)
                },
                new ToastAction { Text = Lang.Get("lang.kitopia.cancel") }
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
            await ShowToastAsync(Lang.Get("lang.kitopia.deletion_failed"), exception.Message, NotificationType.Error);
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
            await ShowToastAsync(Lang.Get("lang.kitopia.cannot_retrieve_available_versions"), Lang.Format("lang.kitopia.unable_to_retrieve_versions_for_plugin_value", plugin.Name), NotificationType.Warning);
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
            await ShowToastAsync(Lang.Get("lang.kitopia.cannot_retrieve_available_versions"), Lang.Format("lang.kitopia.unable_to_retrieve_versions_for_plugin_value", plugin.Name), NotificationType.Warning);
            return;
        }

        var request = new ToastRequest
        {
            Header = Lang.Format("lang.kitopia.download_value", plugin.Name),
            Text = Lang.Get("lang.kitopia.select_a_version_system_compatibility_will_be_checked_before_downloading"),
            NotificationType = NotificationType.Information,
            AutoCloseDelay = null,
            ShowCloseButton = true,
            SelectionOptions = versionOptions,
            SelectedOption = versionOptions[0],
            SelectionConfirmText = Lang.Get("lang.kitopia.ok"),
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
            await ShowToastAsync(Lang.Get("lang.kitopia.plugin_installed"), Lang.Format("lang.kitopia.value_value_downloaded_and_enabled", plugin.Name, version), NotificationType.Success);
            return;
        }

        await ShowToastAsync(
            Lang.Get("lang.kitopia.plugin_download_failed"),
            Lang.Format("lang.kitopia.cannot_download_value_value_try_a_version_compatible_with_this_system", plugin.Name, version),
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
