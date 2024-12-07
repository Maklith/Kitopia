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
using Core.AvaloniaControl.PluginManagerPage;
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
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using Image = SixLabors.ImageSharp.Image;
using JsonSerializer = System.Text.Json.JsonSerializer;
using Point = Avalonia.Point;

namespace Core.ViewModel.Pages;

public class ApiResponse
{
    public bool flag { get; set; }
    public List<OnlinePluginInfo> data { get; set; }
}

public partial class OnlinePluginInfo : ObservableObject
{
    public int Id { set; get; }

    public string AuthorName
    {
        set => throw new NotImplementedException();
        get
        {
            var request = new HttpRequestMessage()
            {
                RequestUri = new Uri($"{ConfigManger.ApiUrl}/api/user/baseInfo"),
                Method = HttpMethod.Get
            };
            request.Headers.Add("id", AuthorId.ToString());
            var async = PluginManager._httpClient.SendAsync(request).GetAwaiter().GetResult();
            var stringAsync = async.Content.ReadAsStringAsync().Result;
            var deserializeObject = (JObject)JsonConvert.DeserializeObject(stringAsync);

            return deserializeObject["data"]["userName"].ToString();
        }
    }

    public int AuthorId { set; get; }
    [ObservableProperty] public Bitmap? _icon;

    public string Name { set; get; }
    public string NameSign { set; get; }
    public bool IsPublic { set; get; }

    public string LastVersion { set; get; }
    public int LastVersionId { set; get; }

    public string DescriptionShort { set; get; }
    public string Description { set; get; }
    public List<string> SupportSystems { set; get; }

    public bool InLocal
    {
        get { return PluginManager.AllPluginInfos.Any(x => x.NameSign == NameSign); }
    }

    public void Upadate()
    {
        OnPropertyChanged(nameof(InLocal));
    }

    public string ToPlgString()
    {
        return $"{Id}_{AuthorId}_{NameSign}";
    }

    public override string ToString()
    {
        return ToPlgString();
    }
}

public partial class MarketPageViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<OnlinePluginInfo> _plugins = new();

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
        var apiResponse = JsonSerializer.Deserialize<ApiResponse>(stringAsync, options);
        if (apiResponse != null && apiResponse.data != null)
            for (var i = 0; i < apiResponse.data.Count; i++)
                Plugins.Add(apiResponse.data[i]);
    }

    [RelayCommand]
    private async Task<bool> DownloadPlugin(OnlinePluginInfo plugin)
    {
        return await PluginManager.DownloadPluginOnline(plugin);
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

            var pluginDetail = new AvaloniaControl.MarketPage.PluginDetail();
            pluginDetail.DataContext = pluginInfo;
            pluginDetail.Content = stackPanel;
            var dialog = new DialogContent()
            {
                Content = pluginDetail,
                Title = "插件详细信息"
            };


            ServiceManager.Services!.GetService<IContentDialog>()!.ShowDialogAsync(childOfType,
                dialog, true);
        }
    }
}