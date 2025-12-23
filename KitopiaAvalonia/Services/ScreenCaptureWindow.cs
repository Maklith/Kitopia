using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Core.SDKs.Services;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace KitopiaAvalonia.Services;

public class ScreenCaptureWindow : IScreenCaptureWindow
{
    public void CaptureScreen()
    {
        var results = ServiceManager.Services.GetService<IScreenCaptureManager>()!.CaptureAllScreenBytes();
        var window = new Windows.ScreenCaptureWindow(results);
        foreach (var result in results)
        {
            result.Source?.Dispose();
        }
        window.Show();

        GC.Collect(2, GCCollectionMode.Aggressive);
    }

    public void RequestUserSelectScreenInfo(Action<ScreenCaptureInfo> action)
    {
        var results = ServiceManager.Services.GetService<IScreenCaptureManager>()!.CaptureAllScreenBytes();
        var window = new Windows.ScreenCaptureWindow(results);
        foreach (var result in results)
        {
            result.Source?.Dispose();
        }
        window.SetToSelectMode(action.Invoke);
        window.Show();
    }

    public void RequestUserSelectScreenBytes(Action<ScreenCaptureResult> action, Action cancle)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var results = ServiceManager.Services.GetService<IScreenCaptureManager>()!.CaptureAllScreenBytes();
            var window = new Windows.ScreenCaptureWindow(results);
            foreach (var result in results)
            {
                result.Source?.Dispose();
            }
            window.SetToSelectBytesMode(action.Invoke, cancle);
            window.Show();
        });
    }

    public async Task<ScreenCaptureInfo> GetScreenCaptureInfo()
    {
        return new ScreenCaptureInfo();
    }
}