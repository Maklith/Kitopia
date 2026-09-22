#region

using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Features.UI.UiControls.Plugin;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Serilog;
using Ursa.Controls;
using PluginInfoUiHelper = Kitopia.Desktop.Features.Services.Plugin.PluginInfoUiHelper;

#endregion

namespace Kitopia.Desktop.Features.ViewModel.Pages.plugin;

public partial class PluginManagerPageViewModel : ObservableRecipient
{
    private static ILogger Logger = LogManager.Logger.ForContext<PluginManagerPageViewModel>();
    public ObservableCollection<PluginInfoUiHelper> Items { get; } = new();

    public PluginManagerPageViewModel()
    {
        RefreshItems();
        WeakReferenceMessenger.Default.Register<PluginsReloaded>(this, static (recipient, _) =>
        {
            var viewModel = (PluginManagerPageViewModel)recipient;
            if (Dispatcher.UIThread.CheckAccess()) viewModel.RefreshItems();
            else Dispatcher.UIThread.Post(viewModel.RefreshItems);
        });
    }

    private void RefreshItems()
    {
        var installed = PluginManager.GetPluginLocalInfos().OrderBy(info => info.ToPlgString()).ToArray();
        foreach (var obsolete in Items.Where(item => !installed.Any(info => info.ToPlgString() == item.PluginBaseInfo.NameSign)).ToArray())
        {
            Items.Remove(obsolete);
            obsolete.Dispose();
        }
        foreach (var info in installed)
        {
            var item = Items.FirstOrDefault(item => item.PluginBaseInfo.NameSign == info.ToPlgString());
            if (item is not null) continue;
            Items.Add(new PluginInfoUiHelper { PluginBaseInfo = info.PluginBaseInfo, PluginLocalInfo = info, IsLocal = true });
        }
    }

    [RelayCommand]
    private async Task RestartApp()
    {
        await ServiceManager.Services.GetService<IApplicationService>()!.RestartAsync();
    }

    [RelayCommand]
    private void Delete(PluginInfoUiHelper pluginInfoEx)
    {
        PluginManager.DeletePlugin(pluginInfoEx.PluginLocalInfo);
    }

    [RelayCommand]
    public async Task Switch(PluginInfoUiHelper pluginInfoUi)
    {
        var pluginInfoEx = pluginInfoUi.PluginLocalInfo;
        Logger.Debug(pluginInfoEx.IsEnabled.ToString());
        if (pluginInfoEx.IsEnabled)
            //卸载插件
            PluginManager.DisablePlugin(pluginInfoEx);
        else
            //加载插件
            //Plugin.NewPlugin(pluginInfoEx.Path, out var weakReference);
            await PluginManager.EnablePluginAsync(pluginInfoEx.ToPlgString());

        Logger.Debug(pluginInfoEx.IsEnabled.ToString());
    }

    [RelayCommand]
    public async Task Update(PluginInfoUiHelper pluginInfoEx)
    {
        await PluginManager.Update(pluginInfoEx.PluginBaseInfo.NameSign);
    }


    [RelayCommand]
    public void ToPluginSettingPage(PluginInfoUiHelper pluginInfoEx)
    {
        if (!pluginInfoEx.PluginLocalInfo.IsEnabled) return;

        ServiceManager.Services?.GetService<INavigationService>()?.Navigate(
            "plugin/settings/select",
            new Dictionary<string, object?>
            {
                ["pluginInfo"] = pluginInfoEx.PluginLocalInfo.ToPlgString()
            });
    }

    [RelayCommand]
    private async Task ShowPluginVersionInfo(Control control)
    {
    }

    [RelayCommand]
    private async Task ShowPluginDetail(PluginInfoUiHelper pluginInfoUiHelper)
    {
        var overlayDialogOptions = new OverlayDialogOptions
        {
            CanLightDismiss = true,
            CanDragMove = false,
            IsCloseButtonVisible = false,
        };
        await OverlayDialog.ShowCustomModal<PluginDetail, PluginDetailViewModel, object>(
            new PluginDetailViewModel(pluginInfoUiHelper), "LocalHost", overlayDialogOptions);
    }
}
