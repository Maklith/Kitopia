using PluginCore.Localization;
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
    [Feature(
        "image-pin",
        "lang.kitopiaex.pin_captured_image",
        "lang.kitopiaex.select_a_screen_region_and_pin_it_in_a_movable_window",
        "lang.kitopia.screenshots_and_images",
        0xf602,
        140,
        Activation = FeatureActivationMode.ScreenCapture)]
    [Capture("lang.kitopiaex.pin_captured_image", 0xf602)]
    public void Pin(ScreenCaptureResult dResult)
    {
        if (dResult.Source is null)
        {
            throw new Exception(Lang.Get("lang.kitopiaex.no_image_data"));
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
