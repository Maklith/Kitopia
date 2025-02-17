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
    public ObservableCollection<PluginInfoUiHelper> Items => new ObservableCollection<PluginInfoUiHelper>(PluginManager.GetPluginLocalInfos().Select(e=>new PluginInfoUiHelper()
    {
        PluginBaseInfo = e.PluginBaseInfo,
        PluginLocalInfo = e
    }).ToList());

    public PluginManagerPageViewModel()
    {
       // PluginManager.CheckAllUpdate();
    }

    [RelayCommand]
    private void RestartApp()
    {
        ServiceManager.Services.GetService<IApplicationService>()!.Restart();
    }

    [RelayCommand]
    private void Delete(PluginInfoUiHelper pluginInfoEx)
    {
        PluginManager.DeletePlugin(pluginInfoEx.PluginLocalInfo);
    }

    [RelayCommand]
    public void Switch(PluginInfoUiHelper pluginInfoUi)
    {
        var pluginInfoEx = pluginInfoUi.PluginLocalInfo;
        Log.Debug(pluginInfoEx.IsEnabled);
        if (pluginInfoEx.IsEnabled)
            //卸载插件
            PluginManager.UnloadPlugin(pluginInfoEx);
        else
            //加载插件
            //Plugin.NewPlugin(pluginInfoEx.Path, out var weakReference);
            PluginManager.EnablePlugin(pluginInfoEx);
        
        Log.Debug(pluginInfoEx.IsEnabled);
    }

    [RelayCommand]
    public async Task Update(PluginInfoUiHelper pluginInfoEx)
    {
        await PluginManager.Update(pluginInfoEx.PluginBaseInfo.Id, pluginInfoEx.PluginBaseInfo.NameSign);
    }


    [RelayCommand]
    public void ToPluginSettingPage(PluginInfoUiHelper pluginInfoEx)
    {
        if (!pluginInfoEx.PluginLocalInfo.IsEnabled) return;

        ((INavigationPageService)ServiceManager.Services!.GetService(typeof(INavigationPageService))).Navigate(
            $"PluginSettingSelectPage_{pluginInfoEx.PluginLocalInfo.ToPlgString()}");
    }

    [RelayCommand]
    private async Task ShowPluginVersionInfo(Control control)
    {
        /*if (control.DataContext is PluginInfo pluginInfo)
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
        }*/
    }

    [RelayCommand]
    private async Task ShowPluginDetail(Control control)
    {
        /*if (control.DataContext is PluginInfo pluginInfo)
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
        }*/
    }
}