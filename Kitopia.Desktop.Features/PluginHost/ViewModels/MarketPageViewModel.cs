using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Desktop.Features.Services.Config;
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
        var async = await PluginNetworkService.HttpClient.GetAsync($"{ConfigManger.ApiUrl}/api/plugin/all");
        var stringAsync = await async.Content.ReadAsStringAsync();
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        var apiResponse = JsonSerializer.Deserialize<OnlinePluginInfo.ApiResponse>(stringAsync, options);
        if (apiResponse != null && apiResponse.data != null)
            for (var i = 0; i < apiResponse.data.Count; i++)
                Plugins.Add(new PluginInfoUiHelper
                {
                    PluginBaseInfo = apiResponse.data[i].ToPluginBaseInfo(),
                    OnlinePluginInfo = apiResponse.data[i],
                    IsLocal = false
                });
    }

    [RelayCommand]
    private async Task<bool> DownloadPlugin(OnlinePluginInfo plugin)
    {
        return await PluginManager.DownloadPluginAndEnable(plugin.Id, plugin.NameSign, plugin.LastVersionId);
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
