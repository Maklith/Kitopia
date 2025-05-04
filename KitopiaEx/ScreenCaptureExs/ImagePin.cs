using System;
using Avalonia;
using Avalonia.Threading;
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

        Dispatcher.UIThread.Invoke((() =>
        {

            var imagePin = new global::KitopiaEx.ImagePin.ImagePin
            {
                Image =
                {
                    Source = dResult.Source.ToAWriteableBitmap()
                }
            };
            imagePin.Position=(new PixelPoint(dResult.Info.X,dResult.Info.Y));
            imagePin.Width = dResult.Info.Width/imagePin.DesktopScaling;
            imagePin.Height = dResult.Info.Height/imagePin.DesktopScaling;
            imagePin.Show();
        }));
    }
}