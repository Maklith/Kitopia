#region

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.AvaloniaControl.PluginManagerPage;
using Core.SDKs;
using Core.SDKs.CustomScenario;
using Core.SDKs.Services;
using Core.SDKs.Services.Config;
using Core.SDKs.Services.Plugin;
using KitopiaAvalonia.Tools;
using log4net;
using Markdown.Avalonia.Full;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PluginCore;
using Path = System.IO.Path;

#endregion

namespace Core.ViewModel.Pages.plugin;

public partial class PluginManagerPageViewModel : ObservableRecipient
{
    private static readonly ILog Log = LogManager.GetLogger(nameof(PluginManagerPageViewModel));
    private readonly TaskScheduler _scheduler = TaskScheduler.FromCurrentSynchronizationContext();
    public ObservableCollection<PluginInfo> Items => PluginManager.AllPluginInfos;

    public PluginManagerPageViewModel()
    {
        PluginManager.CheckAllUpdate();
    }

    [RelayCommand]
    private void RestartApp()
    {
        ServiceManager.Services.GetService<IApplicationService>()!.Restart();
    }

    [RelayCommand]
    private void Delete(PluginInfo pluginInfoEx)
    {
        PluginManager.DeletePlugin(pluginInfoEx);
    }

    [RelayCommand]
    public void Switch(PluginInfo pluginInfoEx)
    {
        if (pluginInfoEx.IsEnabled)
            //卸载插件
            PluginManager.UnloadPlugin(pluginInfoEx);
        else
            //加载插件
            //Plugin.NewPlugin(pluginInfoEx.Path, out var weakReference);
            PluginManager.EnablePluginByInfo(pluginInfoEx);
    }

    [RelayCommand]
    public async Task Update(PluginInfo pluginInfoEx)
    {
        var httpResponseMessage = PluginManager._httpClient
            .GetAsync($"{ConfigManger.ApiUrl}/api/plugin/{pluginInfoEx.Id}").Result;
        var httpContent = httpResponseMessage.Content.ReadAsStringAsync().Result;
        var deserializeObject = (JObject)JsonConvert.DeserializeObject(httpContent);
        pluginInfoEx.UpdateTargetVersion = deserializeObject["data"]["lastVersionId"].ToObject<int>();

        PluginManager.UnloadPlugin(pluginInfoEx);

        if (!pluginInfoEx.UnloadFailed)
        {
            await PluginManager.DownloadPluginOnline(pluginInfoEx);
            var pluginInfoEx2 =
                PluginManager.AllPluginInfos.FirstOrDefault(e => e.ToPlgString() == pluginInfoEx.ToPlgString());
            if (pluginInfoEx2 is null) return;
            PluginManager.EnablePluginByInfo(pluginInfoEx);
        }
    }


    [RelayCommand]
    public void ToPluginSettingPage(PluginInfo pluginInfoEx)
    {
        if (!pluginInfoEx.IsEnabled) return;

        ((INavigationPageService)ServiceManager.Services!.GetService(typeof(INavigationPageService))).Navigate(
            $"PluginSettingSelectPage_{pluginInfoEx.ToPlgString()}");
    }

    [RelayCommand]
    private async Task ShowPluginVersionInfo(Control control)
    {
        if (control.DataContext is PluginInfo pluginInfo)
        {
            var request = new HttpRequestMessage()
            {
                RequestUri =
                    new Uri($"{ConfigManger.ApiUrl}/api/plugin/detail/{pluginInfo.Id}/{pluginInfo.CanUpdateVersionId}"),
                Method = HttpMethod.Get
            };
            request.Headers.Add("AllBeforeThisVersion", true.ToString());
            var sendAsync = await PluginManager._httpClient.SendAsync(request);
            var stringAsync = await sendAsync.Content.ReadAsStringAsync();
            var deserializeObject = (JObject)JsonConvert.DeserializeObject(stringAsync);
            var list = deserializeObject["data"].ToObject<List<JObject>>();
            var stackPanel = new StackPanel();
            stackPanel.Spacing = 4;
            Application.Current.Styles.TryGetResource("TitleLabel", null, out var h1);
            Application.Current.Styles.TryGetResource("SemiColorBorder", null, out var semiColorBorder);
            var semiColorBorder2 = semiColorBorder as SolidColorBrush;
            var controlTheme = h1 as ControlTheme;
            var childOfType = control.GetParentOfType<Window>().GetChildOfType<ContentPresenter>("DialogOvercover");
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

            var dialog = new DialogContent()
            {
                Content = stackPanel,
                Title = "版本详细信息"
            };

            ServiceManager.Services!.GetService<IContentDialog>()!.ShowDialogAsync(childOfType,
                dialog, true);
        }
    }

    [RelayCommand]
    private async Task ShowPluginDetail(Control control)
    {
        if (control.DataContext is PluginInfo pluginInfo)
        {
            var stackPanel = new StackPanel();
            stackPanel.Spacing = 4;

            var request = new HttpRequestMessage()
            {
                RequestUri =
                    new Uri($"{ConfigManger.ApiUrl}/api/plugin/detail/{pluginInfo.Id}/{pluginInfo.CanUpdateVersionId}"),
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

            var pluginDetail = new PluginDetail();
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