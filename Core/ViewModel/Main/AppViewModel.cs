using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Services;
using Core.Services.Config;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Core.ViewModel.Main;

/// <summary>
/// 应用程序视图模型 / Application view model for global app operations
/// </summary>
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