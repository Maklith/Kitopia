using System.Buffers;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using Core.SDKs.Services;
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
    public bool HasText()
    {
        try
        {
            if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime appLifetime)
            {
                return appLifetime.MainWindow.Clipboard.GetFormatsAsync()
                                  .WaitAsync(TimeSpan.FromSeconds(1))
                                  .GetAwaiter()
                                  .GetResult()
                                  .Contains("Text");
            }

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
            {
                return appLifetime.MainWindow.Clipboard.GetTextAsync()
                                  .WaitAsync(TimeSpan.FromSeconds(1))
                                  .GetAwaiter()
                                  .GetResult();
            }

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
        WriteableBitmap? writeableBitmap=null;
        var tcs = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            var bitmapSource = Clipboard.GetImage();
            writeableBitmap = new WriteableBitmap(new PixelSize(bitmapSource.PixelWidth,bitmapSource.PixelHeight),new Vector(96,96));

            using var lockedFramebuffer = writeableBitmap.Lock();
            
            bitmapSource.CopyPixels( new Int32Rect(), lockedFramebuffer.Address,bitmapSource.PixelWidth*bitmapSource.PixelHeight*4 ,(((bitmapSource.PixelWidth * bitmapSource.Format.BitsPerPixel) + 31) & ~31) >> 3);
            tcs.SetResult(true);
           
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        tcs.Task.Wait();
        return writeableBitmap;
       // var writeableBitmap = new WriteableBitmap();
       // bitmapSource.CopyPixels();
        try
        {
            if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime appLifetime)
            {
                byte[] result = [];
                Dispatcher.UIThread.Invoke(() => {
                    result = appLifetime.MainWindow.Clipboard.GetDataAsync("Unknown_Format_2")
                                        .WaitAsync(TimeSpan.FromSeconds(1))
                                        .GetAwaiter()
                                        .GetResult() as byte[];
                });
                var imgInfo = new byte[Marshal.SizeOf<Gdi32.BITMAPINFO>()];
                var img = new byte[result.Length - Marshal.SizeOf<Gdi32.BITMAPINFO>() + 4];
                Array.Copy(result, 40, img, 0, img.Length);
                Array.Copy(result, 0, imgInfo, 0, imgInfo.Length);
                Gdi32.BITMAPINFO info = BytesToStructure<Gdi32.BITMAPINFO>(imgInfo);
                SixLabors.ImageSharp.Configuration configuration = Configuration.Default;
                configuration.PreferContiguousImageBuffers = true;

                var image = SixLabors.ImageSharp.Image.LoadPixelData<Rgba32>(configuration, img, info.bmiHeader.biWidth,
                    info.bmiHeader.biHeight);
                image.Mutate(x => x.Flip(FlipMode.Vertical));
                if (!image.DangerousTryGetSinglePixelMemory(out Memory<Rgba32> memory))
                {
                    throw new Exception(
                        "This can only happen with multi-GB images or when PreferContiguousImageBuffers is not set to true.");
                }

                using (MemoryHandle pinHandle = memory.Pin())
                {
                    unsafe
                    {
                        var pixelFormat = PixelFormat.Bgra8888;
                        var bitmap1 = new Bitmap(pixelFormat, AlphaFormat.Unpremul, (IntPtr)pinHandle.Pointer,
                            new PixelSize(info.bmiHeader.biWidth, info.bmiHeader.biHeight), new Vector(96, 96),
                            ((((info.bmiHeader.biWidth * pixelFormat.BitsPerPixel) + 31) & ~31) >> 3));
                        return bitmap1;
                    }
                }
            }
        }
        catch (Exception e)
        {
            return null;
        }

        return null;
    }

    public bool SetImage(Bitmap image)
    {
        try
        {
            var data2 = new DataObject();
            var memoryStream = new MemoryStream();
            image.Save(memoryStream);
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

    [STAThread]
    public async Task<bool> SetImageAsync(Image image)
    {
        var tcs = new TaskCompletionSource<bool>();
        var thread = new Thread(() => {
            var memoryStream = new MemoryStream();
            image.SaveAsBmp(memoryStream);
            var bitmap = new System.Drawing.Bitmap(memoryStream);

            var bitmapData = bitmap.LockBits(
                new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);
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

    private static T BytesToStructure<T>(byte[] bytes)
    {
        int size = Marshal.SizeOf(typeof(T));
        if (bytes.Length < size)
            throw new Exception("Invalid parameter");

        IntPtr ptr = Marshal.AllocHGlobal(size);
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
        int size = Marshal.SizeOf(structObj);
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(structObj, buffer, false);
            byte[] bytes = new byte[size];
            Marshal.Copy(buffer, bytes, 0, size);
            return bytes;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}