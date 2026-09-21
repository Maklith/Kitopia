using System;
using System.Collections.Generic;
using Avalonia.Threading;
using Kitopia.Desktop.Features.Search.ViewModels;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Windows;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
#if WINDOWS
using Kitopia.Desktop.Platform.Windows;
#endif

namespace Kitopia.Desktop.Services;

public sealed class MouseQuickWindowService : IMouseQuickWindowService
{
    private MouseQuickWindow? _window;

    public void Open()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (_window is { IsVisible: true })
                {
                    _window.Close();
                    return;
                }
#if WINDOWS
                var selection = ServiceManager.Services.GetRequiredService<ExplorerFileSelection>().GetSelection();
                var files = selection.Paths;
#else
                IReadOnlyList<string> files = Array.Empty<string>();
#endif
                if (files.Count == 0) return;
                var window = ServiceManager.Services.GetRequiredService<MouseQuickWindow>();
                _window = window;
                window.Closed += (_, _) => { if (ReferenceEquals(_window, window)) _window = null; };
#if WINDOWS
                window.AnchorBounds = selection.Bounds;
#endif
                window.Show();
                await ((MouseQuickWindowViewModel)window.DataContext!).SetFilesAsync(files);
            }
            catch (Exception exception)
            {
                LogManager.Logger.Error(exception, "打开文件速览失败");
            }
        });
    }
}
