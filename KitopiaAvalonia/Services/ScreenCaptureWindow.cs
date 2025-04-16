using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
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
        while (results.TryPop(out var result))
        {
            var window = new Windows.ScreenCaptureWindow(result);
            result.Source.Dispose();
            window.Show();
        }

        GC.Collect(2, GCCollectionMode.Aggressive);
    }

    public void RequestUserSelectScreenInfo(Action<ScreenCaptureInfo> action)
    {
        var results = ServiceManager.Services.GetService<IScreenCaptureManager>()!.CaptureAllScreenBytes();
        while (results.TryPop(out var result))
        {
            var window = new Windows.ScreenCaptureWindow(result);
            result.Source.Dispose();
            window.SetToSelectMode(action.Invoke);
            window.Show();
        }
    }

    public void RequestUserSelectScreenBytes(Action<ScreenCaptureResult> action,Action cancle)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            var results = ServiceManager.Services.GetService<IScreenCaptureManager>()!.CaptureAllScreenBytes();
            int count = results.Count;
            int cancelCount = 0;
            Lock @lock = new Lock();
            while (results.TryPop(out var result))
            {
                var window = new Windows.ScreenCaptureWindow(result);
                result.Source.Dispose();
                window.SetToSelectBytesMode(action.Invoke, (() =>
                {
                    lock (@lock)
                    {
                        cancelCount++;
                        if (count == cancelCount)
                        {
                            cancle.Invoke();
                        }
                    }

                }));
                window.Show();
            }
        });

    }

    public async Task<ScreenCaptureInfo> GetScreenCaptureInfo()
    {
        return new ScreenCaptureInfo();
    }
}