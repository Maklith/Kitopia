using Avalonia.Threading;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Windows;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Kitopia.Desktop.Services;

public class MouseQuickWindowService : IMouseQuickWindowService
{
    public void Open()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var mouseQuickWindow = ServiceManager.Services.GetService<MouseQuickWindow>();
            mouseQuickWindow.Show();
        });
    }
}