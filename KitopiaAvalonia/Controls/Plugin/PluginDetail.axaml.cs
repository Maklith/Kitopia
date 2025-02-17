using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Core.SDKs.Services.Config;
using Core.SDKs.Services.Plugin;
using Core.ViewModel.Pages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PluginCore;

namespace Core.AvaloniaControl.MarketPage;

public partial class PluginDetail : UserControl
{
    public static AvaloniaProperty<Control> ContentProperty =
        AvaloniaProperty.Register<PluginDetail, Control>(nameof(Content));

    public Control Content
    {
        get => (Control)GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public static AvaloniaProperty<string> MarkdownProperty =
        AvaloniaProperty.Register<PluginDetail, string>(nameof(Markdown));

    public string Markdown
    {
        get => (string)GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public PluginDetail()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is not OnlinePluginInfo pluginInfo) return;

        /*Task.Run(async () =>
        {
            var request = new HttpRequestMessage()
            {
                RequestUri = new Uri($"{ConfigManger.ApiUrl}/api/plugin/{pluginInfo.Id}"),
                Method = HttpMethod.Get
            };
            request.Headers.Add("AllBeforeThisVersion", true.ToString());
            var sendAsync = await PluginManager._httpClient.SendAsync(request);
            var stringAsync = await sendAsync.Content.ReadAsStringAsync();
            var deserializeObject = (JObject)JsonConvert.DeserializeObject(stringAsync);
            var list = deserializeObject["data"]["description"].ToString();
            await Dispatcher.UIThread.InvokeAsync(() => { SetValue(MarkdownProperty, list); });
        });*/
    }
}