using System.Collections.ObjectModel;
using System.Drawing;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.SDKs;
using Core.SDKs.Services;
using Core.SDKs.Services.Config;
using Core.SDKs.Services.Plugin;
using Core.UiControls.Plugin;
using Core.ViewModel.Pages.plugin;
using KitopiaAvalonia.Tools;
using Markdown.Avalonia.Full;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PluginCore;

using Ursa.Controls;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

using JsonSerializer = System.Text.Json.JsonSerializer;
using Point = Avalonia.Point;

namespace Core.ViewModel.Pages;



public partial class OnlinePluginInfo 
{
    internal class ApiResponse
    {
        public bool flag { get; set; }
        public List<OnlinePluginInfo> data { get; set; }
    }
    public int Id { set; get; }

   

    public int AuthorId { set; get; }
    

    public string Name { set; get; }
    public string NameSign { set; get; }
    public bool IsPublic { set; get; }

    public string LastVersion { set; get; }
    public int LastVersionId { set; get; }

    public string DescriptionShort { set; get; }
    public string Description { set; get; }
    public List<string> SupportSystems { set; get; }
    
    public string ToPlgString()
    {
        return $"{Id}_{AuthorId}_{NameSign}";
    }

    public override string ToString()
    {
        return ToPlgString();
    }

    public PluginBaseInfo ToPluginBaseInfo()
    {
        return new PluginBaseInfo()
        {
            Id = Id,
            AuthorId = AuthorId,
            Name = Name,
            NameSign = NameSign
        };
    }
}

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
        var async = await PluginManager._httpClient.GetAsync($"{ConfigManger.ApiUrl}/api/plugin/all");
        var stringAsync = await async.Content.ReadAsStringAsync();
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        var apiResponse = JsonSerializer.Deserialize<OnlinePluginInfo.ApiResponse>(stringAsync, options);
        if (apiResponse != null && apiResponse.data != null)
            for (var i = 0; i < apiResponse.data.Count; i++)
                Plugins.Add(new PluginInfoUiHelper()
                {
                    PluginBaseInfo = apiResponse.data[i].ToPluginBaseInfo(),
                    OnlinePluginInfo = apiResponse.data[i],
                    IsLocal = false
                });
    }

    [RelayCommand]
    private async Task<bool> DownloadPlugin(OnlinePluginInfo plugin)
    {
        return await PluginManager.DownloadPluginOnline(plugin.Id,plugin.NameSign,plugin.LastVersionId);
    }

    [RelayCommand]
    private async Task ShowPluginDetail(PluginInfoUiHelper pluginInfoUiHelper)
    {
        var overlayDialogOptions = new OverlayDialogOptions()
        {
            CanLightDismiss = true,
            
        };
        await OverlayDialog.ShowCustomModal<PluginDetail, PluginDetailViewModel, object>(new PluginDetailViewModel(pluginInfoUiHelper), "LocalHost",overlayDialogOptions);
    }
}