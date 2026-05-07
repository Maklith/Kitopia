using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Core.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Core.ViewModel.Main;

/// <summary>
/// 应用程序视图模型 / Application view model for global app operations
/// </summary>
public partial class AppViewModel : ObservableObject
{
    [RelayCommand]
    public async Task Exit()
    {
        await ServiceManager.Services.GetService<IApplicationService>()!.StopAsync();
    }

    [RelayCommand]
    public void OpenMainWindow()
    {
        var toastService = ServiceManager.Services.GetService<IToastService>();
        if (toastService?.HasUnreadSuppressedNotifications() == true)
        {
            var opened = toastService.TryOpenLatestSuppressedNotification();
            if (!opened)
            {
                WeakReferenceMessenger.Default.Send<PageChangeEventArgs>(new PageChangeEventArgs("DeviceChat"));
                toastService.ClearUnreadSuppressedNotifications();
            }
        }

        if (Application.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow!.Show();
            desktop.MainWindow.WindowState = WindowState.Normal;
            ServiceManager.Services.GetService<IWindowTool>()
                .SetForegroundWindow(desktop.MainWindow.TryGetPlatformHandle().Handle);
        }
    }
}
