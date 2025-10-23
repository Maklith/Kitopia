using System.Runtime.InteropServices;
using System.Threading.RateLimiting;
using System.Windows;
using System.Windows.Media.Imaging;
using Avalonia.Controls.ApplicationLifetimes;
using Core.Services;
using OpenCvSharp;
using PluginCore;
using Polly;
using Polly.Retry;
using Serilog;
using Application = Avalonia.Application;
using PixelFormats = System.Windows.Media.PixelFormats;
using Clipboard = System.Windows.Clipboard;
using Size = OpenCvSharp.Size;

namespace Core.Window;

public class ClipboardWindow : IClipboardService
{
    private static ILogger Log = LogManager.Logger.ForContext<ClipboardWindow>();


    private static readonly ResiliencePipeline ResiliencePipeline = new ResiliencePipelineBuilder()
        .AddConcurrencyLimiter(new ConcurrencyLimiterOptions()
        {
            PermitLimit = 1,
            QueueLimit = Int32.MaxValue
        })
        .AddRetry(
            new RetryStrategyOptions()
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(exception =>
                {
                    Log.Error(exception, "错误");
                    return true;
                }),
                Delay = TimeSpan.FromSeconds(1),
                MaxRetryAttempts = 5,
                BackoffType = DelayBackoffType.Linear,
                UseJitter = true
            }).Build();

    public bool HasText()
    {
        try
        {
            if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime appLifetime)
                return appLifetime.MainWindow.Clipboard.GetFormatsAsync()
                    .WaitAsync(TimeSpan.FromSeconds(1))
                    .GetAwaiter()
                    .GetResult()
                    .Contains("Text");

            return false;
        }
        catch (Exception e)
        {
            return false;
        }
    }

    public string GetText()
    {
        try
        {
            if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime appLifetime)
                return appLifetime.MainWindow.Clipboard.GetTextAsync()
                    .WaitAsync(TimeSpan.FromSeconds(1))
                    .GetAwaiter()
                    .GetResult();

            return null;
        }
        catch (Exception e)
        {
            return null;
        }
    }

    public bool SetText(string text)
    {
        try
        {
            if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime appLifetime)
            {
                appLifetime.MainWindow.Clipboard.SetTextAsync(text)
                    .WaitAsync(TimeSpan.FromSeconds(1))
                    .GetAwaiter()
                    .GetResult();
                return true;
            }

            return false;
        }
        catch (Exception e)
        {
            return false;
        }
    }

    public bool HasImage()
    {
        return Clipboard.ContainsImage();
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
                Log.Error(ex, "读取剪贴板图片失败");
                tcs.SetResult(true);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = false; // keep thread alive until work completes
        thread.Start();
        tcs.Task.Wait();
        return writeableBitmap;
    }

    [STAThread]
    public async Task<bool> SetImageAsync(ScreenCaptureResult screenCaptureResult)
    {
        var executeAsync = await ResiliencePipeline.ExecuteAsync(async (e) =>
        {
            var tcs = new TaskCompletionSource<bool>();
            var thread = new Thread(() =>
            {
                try
                {
                    // Ensure source is valid
                    var src = screenCaptureResult.Source;
                    if (src == null || src.Width == 0 || src.Height == 0)
                    {
                        tcs.SetResult(false);
                        return;
                    }

                    // Ensure we have a 4-channel BGRA buffer
                    Mat bgra = new Mat();
                    if (src.Channels() == 4)
                    {
                        src.CopyTo(bgra);
                    }
                    else if (src.Channels() == 3)
                    {
                        Cv2.CvtColor(src, bgra, ColorConversionCodes.BGR2BGRA);
                    }
                    else if (src.Channels() == 1)
                    {
                        Cv2.CvtColor(src, bgra, ColorConversionCodes.GRAY2BGRA);
                    }
                    else
                    {
                        // Fallback: convert to 4 channels
                        Cv2.CvtColor(src, bgra, ColorConversionCodes.BGR2BGRA);
                    }

                    int width = bgra.Width;
                    int height = bgra.Height;
                    int bytesPerPixel = 4;
                    int stride = width * bytesPerPixel;
                    int bufferSize = stride * height;

                    // Copy native data into managed array so we can premultiply alpha if necessary
                    byte[] pixels = new byte[bufferSize];
                    Marshal.Copy(bgra.Data, pixels, 0, bufferSize);

                    // Check if premultiplication of alpha is needed (any alpha != 255)
                    bool needPremultiply = false;
                    for (int i = 3; i < pixels.Length; i += 4)
                    {
                        if (pixels[i] != 255)
                        {
                            needPremultiply = true;
                            break;
                        }
                    }

                    if (needPremultiply)
                    {
                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            byte a = pixels[i + 3];
                            if (a == 0)
                            {
                                pixels[i] = 0;
                                pixels[i + 1] = 0;
                                pixels[i + 2] = 0;
                            }
                            else if (a < 255)
                            {
                                // premultiply
                                pixels[i] = (byte)((pixels[i] * a + 127) / 255);
                                pixels[i + 1] = (byte)((pixels[i + 1] * a + 127) / 255);
                                pixels[i + 2] = (byte)((pixels[i + 2] * a + 127) / 255);
                            }
                        }
                    }

                    // Create a BitmapSource with premultiplied BGRA (Pbgra32)
                    var bitmapSource = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels,
                        stride);

                    // Place image into clipboard
                    Clipboard.SetImage(bitmapSource);
                    Clipboard.Flush();

                    tcs.SetResult(true);
                }
                catch (Exception exception)
                {
                    Log.Error(exception, "设置剪贴板图片失败");
                    tcs.SetResult(false);
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = false; // Keep thread alive until operation completes for reliability
            thread.Start();
            return await tcs.Task;
        });


        return executeAsync;
    }
}