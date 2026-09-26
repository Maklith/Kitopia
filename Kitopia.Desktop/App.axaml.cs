#if LINUX
    using Kitopia.Desktop.Platform.Linux;
#endif

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Kitopia.Desktop.Features.Indexing;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.ViewModel.Main;
using Kitopia.Desktop.Windows;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
#if WINDOWS
using Vanara.PInvoke;
#endif

namespace Kitopia.Desktop;

public partial class App : Application
{
    private DispatcherTimer? _foregroundIndexTimer;

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
            var index = ServiceManager.Services.GetRequiredService<IIndexService>();
            var navigation = ServiceManager.Services.GetRequiredService<INavigationService>();
            _foregroundIndexTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _foregroundIndexTimer.Tick += (_, _) =>
            {
#if WINDOWS
                var foreground = User32.GetForegroundWindow();
                if (foreground.IsNull)
                {
                    index.SetForegroundPause(false);
                    return;
                }

                User32.GetWindowThreadProcessId(foreground, out var processId);
                var isKitopiaForeground = processId == Environment.ProcessId;
                var isIndexPageForeground = (nint)foreground == desktop.MainWindow?.TryGetPlatformHandle()?.Handle
                                            && navigation.CurrentPageRoute == "index/status";
#else
                var activeWindow = desktop.Windows.FirstOrDefault(window => window.IsActive);
                var isKitopiaForeground = activeWindow is not null;
                var isIndexPageForeground = ReferenceEquals(activeWindow, desktop.MainWindow)
                                            && navigation.CurrentPageRoute == "index/status";
#endif
                index.SetForegroundPause(isKitopiaForeground && !isIndexPageForeground);
            };
            _foregroundIndexTimer.Start();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
        }

        base.OnFrameworkInitializationCompleted();
    }
}
