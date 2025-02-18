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
using Core.UiControls.Plugin;
using KitopiaAvalonia.Tools;
using log4net;
using Markdown.Avalonia.Full;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PluginCore;
using Ursa.Controls;
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
        PluginLocalInfo = e,
        IsLocal = true
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
    }

    [RelayCommand]
    private async Task ShowPluginDetail(PluginInfoUiHelper pluginInfoUiHelper)
    {
        var overlayDialogOptions = new OverlayDialogOptions()
        {
            CanLightDismiss = true
        };
        await OverlayDialog.ShowCustomModal<PluginDetail, PluginDetailViewModel, object>(new PluginDetailViewModel(pluginInfoUiHelper), "LocalHost",overlayDialogOptions);
    }
}