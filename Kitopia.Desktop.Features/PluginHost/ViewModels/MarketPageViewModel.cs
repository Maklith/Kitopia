using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Features.UI.UiControls.Plugin;
using Kitopia.Desktop.Features.ViewModel.Pages.plugin;
using Ursa.Controls;
using JsonSerializer = System.Text.Json.JsonSerializer;
using PluginInfoUiHelper = Kitopia.Desktop.Features.Services.Plugin.PluginInfoUiHelper;

namespace Kitopia.Desktop.Features.ViewModel.Pages;

public partial class MarketPageViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<PluginInfoUiHelper> _plugins = new();

    public MarketPageViewModel()
    {
        LoadPlugins();
    }

    ~MarketPageViewModel()
    {
        for (var i = 0; i < _plugins.Count; i++) _plugins[i].Icon?.Dispose();
    }

    private async Task LoadPlugins()
    {
        var pageNumber = 1;
        PluginPage? page;
        do
        {
            page = await PluginNetworkService.GetPluginsAsync(pageNumber, 100);
            if (page is null)
            {
                return;
            }

            foreach (var plugin in page.Items)
            {
                Plugins.Add(new PluginInfoUiHelper
                {
                    PluginBaseInfo = plugin.ToPluginBaseInfo(),
                    OnlinePluginInfo = plugin,
                    IsLocal = false
                });
            }

            pageNumber++;
        } while (pageNumber <= page.TotalPages);
    }

    [RelayCommand]
    private async Task<bool> DownloadPlugin(OnlinePluginInfo plugin)
    {
        return await PluginManager.DownloadPluginAndEnable(plugin.NameSign, plugin.LastVersion);
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
