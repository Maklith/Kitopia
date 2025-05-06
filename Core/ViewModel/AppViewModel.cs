using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.SDKs.Services;
using Core.SDKs.Services.Config;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Core.ViewModel;

public partial class AppViewModel : ObservableObject
{
    [RelayCommand]
    public void Exit()
    {
        ConfigManger.Save();
        Environment.Exit(0);
    }

    [RelayCommand]
    public void OpenMainWindow()
    {
        if (Application.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow!.Show();
            desktop.MainWindow.WindowState = WindowState.Normal;
            ServiceManager.Services.GetService<IWindowTool>()
                .SetForegroundWindow(desktop.MainWindow.TryGetPlatformHandle().Handle);
        }
    }
}