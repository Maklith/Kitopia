using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Core.SDKs.Services;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace KitopiaAvalonia.Services;

public class ScreenCaptureWindow : IScreenCaptureWindow
{
    public void CaptureScreen()
    {
        var captureAllScreen = ServiceManager.Services.GetService<IScreenCapture>()!.CaptureAllScreen();
        while (captureAllScreen.TryPop(out var result))
        {
            var window = new Windows.ScreenCaptureWindow(0)
            {
                Position = new PixelPoint(result.Info.X, result.Info.Y),
                WindowState = WindowState.FullScreen,
                SystemDecorations = SystemDecorations.None,
                Background = new SolidColorBrush(Colors.Black),
                ShowInTaskbar = false,
                Topmost = false,
                CanResize = false,
                IsVisible = true,
            };
            window.Image.Source = result.Source;
            window.Show();
        }
        
        GC.Collect(2, GCCollectionMode.Aggressive);
    }

    public async Task<ScreenCaptureInfo> GetScreenCaptureInfo()
    {
        return new ScreenCaptureInfo();
    }
}