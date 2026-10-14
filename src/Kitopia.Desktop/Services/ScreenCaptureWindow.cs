using System;
using System.Threading.Tasks;
using System.Threading;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Kitopia.Desktop.Services;

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
        Dispatcher.UIThread.Invoke((() =>
        {
            var results = ServiceManager.Services.GetService<IScreenCaptureManager>()!.CaptureAllScreenBytes();
            var window = new Windows.ScreenCaptureWindow(results);
            foreach (var result in results)
            {
                result.Source?.Dispose();
            }

            window.SetToSelectMode(action.Invoke);
            window.Show();
        }));

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

    public async Task<ScreenCaptureResult> RequestUserSelectScreenBytesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<ScreenCaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Windows.ScreenCaptureWindow? window = null;
        using var registration = cancellationToken.Register(() =>
        {
            if (completion.TrySetCanceled(cancellationToken))
                Dispatcher.UIThread.Post(() => window?.Close());
        });
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (completion.Task.IsCompleted) return;
            var results = ServiceManager.Services.GetRequiredService<IScreenCaptureManager>().CaptureAllScreenBytes();
            try
            {
                window = new Windows.ScreenCaptureWindow(results);
            }
            finally
            {
                foreach (var result in results) result.Source?.Dispose();
            }
            window.SetToSelectBytesMode(result =>
            {
                if (!completion.TrySetResult(result)) result.Source?.Dispose();
            }, () => completion.TrySetCanceled());
            window.Show();
        });
        return await completion.Task.ConfigureAwait(false);
    }

    public async Task<ScreenCaptureInfo> GetScreenCaptureInfo()
    {
        return new ScreenCaptureInfo();
    }
}
