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
using KitopiaAvalonia.Tools;
using Markdown.Avalonia.Full;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PluginCore;
using SixLabors.ImageSharp;
using Ursa.Controls;
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using Image = SixLabors.ImageSharp.Image;
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
                    OnlinePluginInfo = apiResponse.data[i]
                });
    }

    [RelayCommand]
    private async Task<bool> DownloadPlugin(OnlinePluginInfo plugin)
    {
        return await PluginManager.DownloadPluginOnline(plugin.Id,plugin.NameSign,plugin.LastVersionId);
    }

    [RelayCommand]
    private async Task ShowPluginDetail(Control control)
    {
        if (control.DataContext is OnlinePluginInfo pluginInfo)
        {
            var stackPanel = new StackPanel();
            stackPanel.Spacing = 4;

            var request = new HttpRequestMessage()
            {
                RequestUri =
                    new Uri($"{ConfigManger.ApiUrl}/api/plugin/detail/{pluginInfo.Id}/{pluginInfo.LastVersionId}"),
                Method = HttpMethod.Get
            };
            request.Headers.Add("AllBeforeThisVersion", true.ToString());
            var sendAsync = await PluginManager._httpClient.SendAsync(request);
            var stringAsync = await sendAsync.Content.ReadAsStringAsync();
            var deserializeObject = (JObject)JsonConvert.DeserializeObject(stringAsync);
            var list = deserializeObject["data"].ToObject<List<JObject>>();

            Application.Current.Styles.TryGetResource("TitleLabel", null, out var h1);
            Application.Current.Styles.TryGetResource("SemiColorBorder", null, out var semiColorBorder);
            var semiColorBorder2 = semiColorBorder as SolidColorBrush;
            var controlTheme = h1 as ControlTheme;
            var childOfType = control.GetParentOfType<Window>().GetChildOfType<ContentPresenter>("DialogOvercover");
            stackPanel.Children.Add(new Label()
            {
                Classes = { "H2" },
                Theme = controlTheme,
                Content = "版本说明"
            });
            stackPanel.Children.Add(new Line()
            {
                Stroke = semiColorBorder2,
                EndPoint = new Point(childOfType.Bounds.Width, 0)
            });
            for (var i = 0; i < list.Count; i++)
            {
                stackPanel.Children.Add(new Label()
                {
                    Classes = { "H3" },
                    Theme = controlTheme,
                    Content = list[i]["version"]
                });
                stackPanel.Children.Add(new Line()
                {
                    Stroke = semiColorBorder2,
                    EndPoint = new Point(childOfType.Bounds.Width, 0)
                });
                stackPanel.Children.Add(new MarkdownScrollViewer()
                {
                    Markdown = list[i]["detail"].ToString()
                });
            }

            /*var pluginDetail = new AvaloniaControl.MarketPage.PluginDetail();
            pluginDetail.DataContext = pluginInfo;
            pluginDetail.Content = stackPanel;
            var dialog = new DialogContent()
            {
                Content = pluginDetail,
                Title = "插件详细信息"
            };
            var options = new OverlayDialogOptions()
            {
                FullScreen = FullScreen,
                HorizontalAnchor = HorizontalAnchor,
                VerticalAnchor = VerticalAnchor,
                HorizontalOffset = HorizontalOffset,
                VerticalOffset = VerticalOffset,
                Title = Title,
                CanLightDismiss = CanLightDismiss,
                CanDragMove = CanDragMove,
                IsCloseButtonVisible = IsCloseButtonVisible,
                CanResize = CanResize,
            };
            if (IsModal)
            {
                await OverlayDialog.ShowCustomModal<PluginDetail, OnlinePluginInfo, object>(pluginDetail, dialogHostId, options: options);
            }
            ServiceManager.Services!.GetService<IContentDialog>()!.ShowDialogAsync(childOfType,
                dialog, true);*/
        }
    }
}