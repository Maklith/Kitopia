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

public partial class MarketPageViewModel : ObservableObject
{
    private const int DefaultPageSize = 20;

    [ObservableProperty] private ObservableCollection<PluginInfoUiHelper> _plugins = new();
    [ObservableProperty] private int _currentPage = 1;
    [ObservableProperty] private int _totalPages = 1;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _pageSize = DefaultPageSize;
    [ObservableProperty] private bool _isLoading;

    private int _loadGeneration;

    public IReadOnlyList<int> PageSizeOptions { get; } = [20, 40, 80, 100];

    public MarketPageViewModel()
    {
        _ = LoadPluginsAsync();
    }

    ~MarketPageViewModel()
    {
        for (var i = 0; i < _plugins.Count; i++) _plugins[i].Icon?.Dispose();
    }

    partial void OnCurrentPageChanged(int value)
    {
        _ = LoadPluginsAsync();
    }

    partial void OnPageSizeChanged(int value)
    {
        if (value <= 0)
        {
            return;
        }

        if (CurrentPage == 1)
        {
            _ = LoadPluginsAsync();
            return;
        }

        CurrentPage = 1;
    }

    private async Task LoadPluginsAsync()
    {
        var generation = ++_loadGeneration;
        IsLoading = true;
        try
        {
            var page = await PluginNetworkService.GetPluginsAsync(CurrentPage, PageSize);
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
            new PluginDetailViewModel(pluginInfoUiHelper), "LocalHost", overlayDialogOptions);
    }
}
