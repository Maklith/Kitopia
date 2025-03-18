#if LINUX
    using Core.Linux;
#endif

using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Core.SDKs.Services;
using Core.ViewModel;
using Core.ViewModel.Pages;
using Core.ViewModel.Pages.customScenario;
using Core.ViewModel.Pages.plugin;
using Core.ViewModel.TaskEditor;
using HotAvalonia;
using Kitopia.Services;
using KitopiaAvalonia.Pages;
using KitopiaAvalonia.Services;
using KitopiaAvalonia.Windows;

using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using ScreenCaptureWindow = KitopiaAvalonia.Services.ScreenCaptureWindow;
#if WINDOWS
using Core.Window;
using Core.Window.Everything;
#endif

namespace KitopiaAvalonia;

public partial class App : Application
{
    

    public override void Initialize()
    {
       // this.EnableHotReload();
        AvaloniaXamlLoader.Load(this);
    }


    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = ServiceManager.Services.GetService<MainWindow>();
            DataContext = new AppViewModel();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
        }

        base.OnFrameworkInitializationCompleted();
    }
}