#if LINUX
    using Core.Linux;
#endif

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Core.ViewModel.Main;
using KitopiaAvalonia.Windows;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
#if WINDOWS
#endif

namespace KitopiaAvalonia;

public partial class App : Application
{
    public override void Initialize()
    {
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