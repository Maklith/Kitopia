using System;
using System.Collections.Generic;
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
        var results = ServiceManager.Services.GetService<IScreenCaptureManager>()!.CaptureAllScreenBitmap();
        while (results.TryPop(out var result))
        {
            var window = new Windows.ScreenCaptureWindow(result.Info);
            window.Image.Source = result.Source;
            window.Show();
        }

        GC.Collect(2, GCCollectionMode.Aggressive);
    }

    public void RequestUserSelectScreenInfo(Action<ScreenCaptureInfo> action)
    {
        var results = ServiceManager.Services.GetService<IScreenCaptureManager>()!.CaptureAllScreenBitmap();
        while (results.TryPop(out var result))
        {
            var window = new Windows.ScreenCaptureWindow(result.Info);
            window.Image.Source = result.Source;
            window.SetToSelectMode(action.Invoke);
            window.Show();
        }
    }

    public async Task<ScreenCaptureInfo> GetScreenCaptureInfo()
    {
        return new ScreenCaptureInfo();
    }
}