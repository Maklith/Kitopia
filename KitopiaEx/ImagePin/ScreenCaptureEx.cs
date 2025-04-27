using System;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using PluginCore;
using PluginCore.Attribute;
using PluginCore.ExMethod;

namespace KitopiaEx.ImagePin;

public class ScreenCaptureEx
{
    [Capture("置顶图片", 0xf602)]
    public void Pin(ScreenCaptureResult dResult)
    {
        if (dResult.Source is null)
        {
            throw new Exception("无图像数据");
        }

        Dispatcher.UIThread.Invoke((() =>
        {

            var imagePin = new ImagePin();
            imagePin.Image.Source = dResult.Source.ToAWriteableBitmap();
            var scaleX=1920f/dResult.Info.ScreenInfo.Width;
            var scaleY=1080f/dResult.Info.ScreenInfo.Height;
            
            imagePin.Position=(new PixelPoint(dResult.Info.X,dResult.Info.Y));
            imagePin.Width = dResult.Info.Width/imagePin.DesktopScaling;
            imagePin.Height = dResult.Info.Height/imagePin.DesktopScaling;
            imagePin.Show();
        }));
    }
}