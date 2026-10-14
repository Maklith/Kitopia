using System.Runtime.InteropServices;
using System.Threading.RateLimiting;
using System.Windows;
using System.Windows.Media.Imaging;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Kitopia.Desktop.Features.Services;
using OpenCvSharp;
using PluginCore;
using Polly;
using Polly.Retry;
using Serilog;
using Application = Avalonia.Application;
using Clipboard = System.Windows.Clipboard;
using PixelFormats = System.Windows.Media.PixelFormats;
using Size = OpenCvSharp.Size;

namespace Kitopia.Desktop.Platform.Windows;

public class ClipboardWindow : IClipboardService
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<ClipboardWindow>();


    private static readonly ResiliencePipeline ResiliencePipeline = new ResiliencePipelineBuilder()
        .AddConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = Int32.MaxValue
        })
        .AddRetry(
            new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<ExternalException>(exception =>
                    exception.ErrorCode is
                        unchecked((int)0x800401D0) or // CLIPBRD_E_CANT_OPEN
                        unchecked((int)0x800401D1) or // CLIPBRD_E_CANT_EMPTY
                        unchecked((int)0x800401D2) or // CLIPBRD_E_CANT_SET
                        unchecked((int)0x800401D4)),  // CLIPBRD_E_CANT_CLOSE
                Delay = TimeSpan.FromSeconds(1),
                MaxRetryAttempts = 5,
                BackoffType = DelayBackoffType.Linear,
                UseJitter = true
            }).Build();

    public bool HasText()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime appLifetime)
                return Dispatcher.UIThread.Invoke((() =>
                {
                    return appLifetime.MainWindow is { Clipboard: not null } && appLifetime.MainWindow.Clipboard
                        .GetDataFormatsAsync()
                        .WaitAsync(TimeSpan.FromSeconds(1))
                        .GetAwaiter()
                        .GetResult()
                        .Any(format =>
                            format.Identifier == DataFormats.Text || format.Identifier == DataFormats.UnicodeText);

                }));
                
            return false;
        }
        catch (Exception e)
        {
            Logger.Error(e, "检查剪贴板文本时发生错误");
            return false;
        }
    }

    public string? GetText()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime appLifetime)
                return Dispatcher.UIThread.Invoke((() => appLifetime.MainWindow?.Clipboard?.TryGetTextAsync()
                    .WaitAsync(TimeSpan.FromSeconds(1))
                    .GetAwaiter()
                    .GetResult()));
                

            return null;
        }
        catch (Exception e)
        {
            Logger.Error(e, "获取剪贴板文本时发生错误");
            return null;
        }
    }

    public bool SetText(string text)
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime appLifetime)
            {
                return Dispatcher.UIThread.Invoke(() =>
                {
                    appLifetime.MainWindow?.Clipboard?.SetTextAsync(text)
                        .WaitAsync(TimeSpan.FromSeconds(1))
                        .GetAwaiter()
                        .GetResult();
                    return true;
                });

            }

            return false;
        }
        catch (Exception e)
        {
            Logger.Error(e, "设置剪贴板文本时发生错误");
            return false;
        }
    }

    public bool HasFiles()
    {
        bool result = false;
        var tcs = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            try
            {
                result = Clipboard.ContainsFileDropList();
            }
            catch (Exception)
            {
                result = false;
            }
            finally
            {
                tcs.SetResult(true);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        tcs.Task.Wait();
        return result;
    }

    public IReadOnlyList<string> GetFiles()
    {
        var files = new List<string>();
        var tcs = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    var list = Clipboard.GetFileDropList();
                    foreach (string? path in list)
                    {
                        if (string.IsNullOrEmpty(path)) {
                            continue;
                        }
                        files.Add(path);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "读取剪贴板文件列表失败");
            }
            finally
            {
                tcs.SetResult(true);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        tcs.Task.Wait();
        return files;
    }

    public bool HasImage()
    {
        bool result = false;
        var tcs = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            try
            {
                result = Clipboard.ContainsImage();
            }
            catch (Exception)
            {
                result = false;
            }
            finally
            {
                tcs.SetResult(true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        tcs.Task.Wait();
        return result;
    }

    [STAThread]
    public Mat? GetImage()
    {
        Mat? writeableBitmap = null;
        var tcs = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            try
            {
                var bitmapSource = Clipboard.GetImage();
                if (bitmapSource == null)
                {
                    tcs.SetResult(true);
                    return;
                }

                int width = bitmapSource.PixelWidth;
                int height = bitmapSource.PixelHeight;
                if (width == 0 || height == 0)
                {
                    tcs.SetResult(true);
                    return;
                }

                // We will request 4 bytes per pixel (BGRA)
                int bytesPerPixel = 4;
                int stride = width * bytesPerPixel;
                int bufferSize = stride * height;

                // Create a Mat with 4 channels (CV_8UC4)
                var mat = new Mat(new Size(width, height), MatType.CV_8UC4);

                // Copy pixels directly into the Mat's buffer using the desired stride
                bitmapSource.CopyPixels(new Int32Rect(0, 0, width, height), mat.Data, bufferSize, stride);

                writeableBitmap = mat;
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "读取剪贴板图片失败");
                tcs.SetResult(true);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = false; // keep thread alive until work completes
        thread.Start();
        tcs.Task.Wait();
        return writeableBitmap;
    }

    public async Task<bool> SetImageAsync(ScreenCaptureResult screenCaptureResult)
    {
        try
        {
            return await ResiliencePipeline.ExecuteAsync(async _ =>
            {
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var thread = new Thread(() =>
                {
                    try
                    {
                        var src = screenCaptureResult.Source;
                        if (src == null || src.IsDisposed || src.Empty())
                        {
                            tcs.SetResult(false);
                            return;
                        }

                        var type = src.Type();
                        var pixelFormat = type == MatType.CV_8UC4 ? PixelFormats.Pbgra32
                            : type == MatType.CV_8UC3 ? PixelFormats.Bgr24
                            : type == MatType.CV_8UC1 ? PixelFormats.Gray8
                            : throw new ArgumentException($"不支持的剪贴板图片类型: {type}。", nameof(screenCaptureResult));

                        int width = src.Width;
                        int height = src.Height;
                        int stride = checked((int)src.Step());
                        int bufferSize = checked(stride * (height - 1) + width * src.Channels());

                        var bitmapSource = BitmapSource.Create(width, height, 96, 96, pixelFormat,
                            null, src.Data, bufferSize, stride);

                        // SetImage already persists the data through SetDataObject(copy: true).
                        Clipboard.SetImage(bitmapSource);
                        tcs.SetResult(true);
                    }
                    catch (Exception exception)
                    {
                        tcs.SetException(exception);
                    }
                });

                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = false; // Keep thread alive until operation completes for reliability
                thread.Start();
                return await tcs.Task.ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "设置剪贴板图片失败，错误码 {HResult:X8}", exception.HResult);
            return false;
        }
    }
}
