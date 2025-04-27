using System.Buffers;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.RateLimiting;
using System.Windows;
using System.Windows.Media.Imaging;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using Core.SDKs.Services;
using PluginCore;
using Polly;
using Polly.Retry;
using Serilog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Vanara.PInvoke;
using Application = Avalonia.Application;
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using PixelFormat = Avalonia.Platform.PixelFormat;
using PixelFormats = System.Windows.Media.PixelFormats;
using Rectangle = System.Drawing.Rectangle;
using Vector = Avalonia.Vector;
using Clipboard = System.Windows.Clipboard;
using DataFormats = Avalonia.Input.DataFormats;
using WriteableBitmap = Avalonia.Media.Imaging.WriteableBitmap;

namespace Core.Window;

public class ClipboardWindow : IClipboardService
{
    private static ILogger Log =   LogManager.Logger.ForContext<ClipboardWindow>();
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
    public Bitmap? GetImage()
    {
        WriteableBitmap? writeableBitmap = null;
        var tcs = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            var bitmapSource = Clipboard.GetImage();
            writeableBitmap = new WriteableBitmap(new PixelSize(bitmapSource.PixelWidth, bitmapSource.PixelHeight),
                new Vector(96, 96));

            using var lockedFramebuffer = writeableBitmap.Lock();

            bitmapSource.CopyPixels(new Int32Rect(), lockedFramebuffer.Address,
                bitmapSource.PixelWidth * bitmapSource.PixelHeight * 4,
                ((bitmapSource.PixelWidth * bitmapSource.Format.BitsPerPixel + 31) & ~31) >> 3);
            tcs.SetResult(true);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        tcs.Task.Wait();
        return writeableBitmap;
        
    }

    public bool SetImage(Bitmap image)
    {
        try
        {
            var data2 = new DataObject();
            var memoryStream = new MemoryStream();
            image.Save(memoryStream, 100);
            var bitmap = new System.Drawing.Bitmap(memoryStream);

            var bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly, bitmap.PixelFormat);

            var bitmapSource = BitmapSource.Create(
                bitmapData.Width, bitmapData.Height,
                bitmap.HorizontalResolution, bitmap.VerticalResolution,
                PixelFormats.Bgr24, null,
                bitmapData.Scan0, bitmapData.Stride * bitmapData.Height, bitmapData.Stride);

            bitmap.UnlockBits(bitmapData);
            bitmap.Dispose();

            data2.SetImage(bitmapSource);
            Ole32.OleSetClipboard(data2);
        }
        catch (Exception e)
        {
            return false;
        }


        return true;
    }
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
                    Log.Error(exception,"错误");
                    return true;
                }),
                Delay = TimeSpan.FromSeconds(1),
                MaxRetryAttempts = 5,
                BackoffType = DelayBackoffType.Linear,
                UseJitter = true
            }).Build();
    [STAThread]
    public async Task<bool> SetImageAsync(Image image)
    {
        var tcs = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            var memoryStream = new MemoryStream();

            image.SaveAsBmp(memoryStream);
            var bitmap = new System.Drawing.Bitmap(memoryStream);

            var bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly, bitmap.PixelFormat);
            var bitmapSource = BitmapSource.Create(
                bitmapData.Width, bitmapData.Height,
                bitmap.HorizontalResolution, bitmap.VerticalResolution,
                PixelFormats.Bgr24, null,
                bitmapData.Scan0, bitmapData.Stride * bitmapData.Height, bitmapData.Stride);

            bitmap.UnlockBits(bitmapData);
            bitmap.Dispose();
            Clipboard.SetImage(bitmapSource);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return await tcs.Task;
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
                    unsafe
                    {
                        int channels = screenCaptureResult.Source.Channels(); // 3或4
                        int stride = (screenCaptureResult.Source.Width * channels + 3) & ~3; // 对齐后的步长
                        int bufferSize = stride * screenCaptureResult.Source.Height;
                        Log.Information("设置剪贴板图片");
                        var bitmapSource = new System.Windows.Media.Imaging.WriteableBitmap(screenCaptureResult.Source.Width,screenCaptureResult.Source.Height,96,96,PixelFormats.Bgra32, null);
                        bitmapSource.Lock();
                       

                        bitmapSource.WritePixels(new Int32Rect(0,0,screenCaptureResult.Source.Width,screenCaptureResult.Source.Height),
                           (IntPtr) screenCaptureResult.Source.DataPointer,
                           bufferSize, stride);
                        bitmapSource.Unlock();
                        
                        Clipboard.SetImage(bitmapSource);
                        Clipboard.Flush();
                        tcs.SetResult(true); // 仅在成功时设置
                    }
                }
                catch (Exception exception)
                {
                    Log.Error(exception, "设置剪贴板图片失败");
                    tcs.SetResult(false); // 失败时设置
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true; // 确保线程自动回收
            thread.Start();
            return await tcs.Task;
        });


        return executeAsync;
    }
    private static T BytesToStructure<T>(byte[] bytes)
    {
        var size = Marshal.SizeOf(typeof(T));
        if (bytes.Length < size)
            throw new Exception("Invalid parameter");

        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(bytes, 0, ptr, size);
            return (T)Marshal.PtrToStructure(ptr, typeof(T));
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public static byte[] StructToBytes(object structObj)
    {
        var size = Marshal.SizeOf(structObj);
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(structObj, buffer, false);
            var bytes = new byte[size];
            Marshal.Copy(buffer, bytes, 0, size);
            return bytes;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}