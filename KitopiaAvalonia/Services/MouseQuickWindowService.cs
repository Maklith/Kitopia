using Avalonia.Threading;
using Core.Services.Interfaces;
using KitopiaAvalonia.Windows;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace KitopiaAvalonia.Services;

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