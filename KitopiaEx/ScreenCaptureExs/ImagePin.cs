using System;
using Avalonia;
using Avalonia.Threading;
using OpenCvSharp;
using PluginCore;
using PluginCore.Attribute;
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
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var imagePin = new global::KitopiaEx.ImagePin.ImagePin
            {
                Image =
                {
                    Source = src.ToAWriteableBitmap()
                }
            };
            if (info != null)
            {
                imagePin.Position=(new PixelPoint(info.Value.X,info.Value.Y));
                imagePin.Width = info.Value.Width/imagePin.DesktopScaling;
                imagePin.Height = info.Value.Height/imagePin.DesktopScaling;
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