using System.Collections.ObjectModel;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Services.Plugin;
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

    private int _loadGeneration;
    private CancellationTokenSource? _searchCts;

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

    public MarketPageViewModel()
    {
        _selectedPlatform = PlatformOptions[0];
        _ = LoadPluginsAsync();
    }

    ~MarketPageViewModel()
    {
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
        OnPropertyChanged(nameof(HasNoPlugins));
        try
        {
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
                plugin.Icon?.Dispose();
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
        finally
        {
            if (generation == _loadGeneration)
            {
                IsLoading = false;
                OnPropertyChanged(nameof(HasNoPlugins));
            }
        }
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
            CanLightDismiss = true
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
}
