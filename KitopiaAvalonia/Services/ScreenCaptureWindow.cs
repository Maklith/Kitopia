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
    public void CaptureScreen(Stack<ScreenCaptureResult> results)
    {
        while (results.TryPop(out var result))
        {
            var window = new Windows.ScreenCaptureWindow(result.Info);
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