using Avalonia;
using Avalonia.Controls.Notifications;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Desktop.Features.Services.Plugin;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Ursa.Controls;
using PluginInfoUiHelper = Kitopia.Desktop.Features.Services.Plugin.PluginInfoUiHelper;

namespace Kitopia.Desktop.Features.ViewModel.Pages.plugin;

public partial class PluginDetailViewModel : ObservableObject
{
    public PluginInfoUiHelper? PluginInfo { get; init; }
    private readonly Action<string>? _onSearchAuthor;

    [ObservableProperty]
    private bool _isInstalling;

    public PluginDetailViewModel(PluginInfoUiHelper pluginStr, Action<string>? onSearchAuthor = null)
    {
        PluginInfo = pluginStr;
        _onSearchAuthor = onSearchAuthor;
    }

    [RelayCommand]
    private void SearchAuthor(object? parameter)
    {
        var authorIdentifier = !string.IsNullOrWhiteSpace(PluginInfo?.OnlinePluginInfo?.AuthorUserName)
            ? PluginInfo.OnlinePluginInfo.AuthorUserName
            : PluginInfo?.AuthorName;

        if (string.IsNullOrWhiteSpace(authorIdentifier)) return;

        if (parameter is DialogControlBase dialog)
        {
            dialog.Close();
        }
        else if (parameter is Visual visual)
        {
            visual.FindAncestorOfType<DialogControlBase>()?.Close();
        }

        _onSearchAuthor?.Invoke(authorIdentifier);
    }

    [RelayCommand]
    private async Task InstallVersion(VersionDetail? versionDetail)
    {
        if (versionDetail is null || PluginInfo is null) return;
        var version = versionDetail.Version;
        if (string.IsNullOrWhiteSpace(version)) return;

        IsInstalling = true;
        try
        {
            var success = await PluginManager.DownloadPluginAndEnable(PluginInfo.PluginBaseInfo.NameSign, version);
            var toastService = ServiceManager.Services.GetService<IToastService>();
            var windowTool = ServiceManager.Services.GetService<IWindowTool>();
            if (success)
            {
                if (toastService != null)
                {
                    await toastService.Show(new ToastRequest
                    {
                        Header = "插件已安装",
                        Text = $"{PluginInfo.PluginBaseInfo.Name} {version} 已成功安装并启用。",
                        NotificationType = NotificationType.Success
                    }, windowTool?.GetForegroundWindow());
                }
            }
            else
            {
                if (toastService != null)
                {
                    await toastService.Show(new ToastRequest
                    {
                        Header = "插件安装失败",
                        Text = $"无法下载安装 {PluginInfo.PluginBaseInfo.Name} {version}。",
                        NotificationType = NotificationType.Warning
                    }, windowTool?.GetForegroundWindow());
                }
            }
        }
        finally
        {
            IsInstalling = false;
        }
    }
}
