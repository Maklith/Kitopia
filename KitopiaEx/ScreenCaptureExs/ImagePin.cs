using System;
using Avalonia;
using Avalonia.Threading;
using OpenCvSharp;
using PluginCore;
using PluginCore.CustomScenario.Attribute;
using PluginCore.ExMethod;

namespace KitopiaEx.ScreenCaptureExs;

public class ImagePin
{
    [Capture("置顶图片", 0xf602)]
    public void Pin(ScreenCaptureResult dResult)
    {
        if (dResult.Source is null)
        {
            throw new Exception("无图像数据");
        }
        PinBase(dResult.Source,dResult.Info);
    }

    internal void PinBase(Mat src,ScreenCaptureInfo? info=null)
    {
        var aWriteableBitmap = src.ToAWriteableBitmap();
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var imagePin = new global::KitopiaEx.ImagePin.ImagePin
            {
                Image =
                {
                    Source = aWriteableBitmap
                }
            };
            if (info != null)
            {
                if (info.Value.RequestRect != null) {
                    imagePin.Position =
                        (new PixelPoint(info.Value.RequestRect.Value.X, info.Value.RequestRect.Value.Y));
                    imagePin.Width = info.Value.RequestRect.Value.Width / imagePin.DesktopScaling;
                    imagePin.Height = info.Value.RequestRect.Value.Height / imagePin.DesktopScaling;
                }
            }
            else
            {
                imagePin.Width = src.Width/imagePin.DesktopScaling;
                imagePin.Height = src.Height/imagePin.DesktopScaling;
            }
            
            imagePin.Show();
        });
    }
}