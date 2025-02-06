using System;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using PluginCore;
using PluginCore.Attribute;

namespace KitopiaEx.ImagePin;

public class ScreenCaptureEx
{
    [Capture("置顶图片", 0xf602)]
    public void Pin(ScreenCaptureResult dResult)
    {
        if (dResult.Bytes is null&& dResult.Source is null)
        {
            throw new Exception("无图像数据");
        }

        Dispatcher.UIThread.Invoke((() =>
        {
            if (dResult.Source is null)
            {

                var writeableBitmap = new WriteableBitmap(
                    new PixelSize(dResult.Info.Width, dResult.Info.Height),
                    new Vector(96, 96), PixelFormat.Bgra8888);
                using (var l = writeableBitmap.Lock())
                {
                    unsafe
                    {
                        var destinationSizeInBytes = dResult.Info.Width * 4 * dResult.Info.Height;
                        fixed (byte* srcPtr = dResult.Bytes)
                        {
                            Buffer.MemoryCopy(srcPtr, (void*)l.Address, destinationSizeInBytes, destinationSizeInBytes);
                        }

                    }
                }

                dResult.Source = writeableBitmap;

            }

            var imagePin = new ImagePin();
            imagePin.Image.Source = dResult.Source;
            imagePin.Width = dResult.Info.Width;
            imagePin.Height = dResult.Info.Height;
            imagePin.Show();
        }));
    }
}