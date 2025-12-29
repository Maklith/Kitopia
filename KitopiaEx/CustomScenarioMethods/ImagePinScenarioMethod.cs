using System;
using System.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PluginCore;
using PluginCore.Attribute;
using PluginCore.ExMethod;

namespace KitopiaEx.CustomScenarioMethods;

public class ImagePinScenarioMethod
{
    [ScenarioMethod("创建图片置顶窗口",$"return=图片置顶窗口实例")]
    public ImagePin.ImagePin OcrResultShow(CancellationToken ct)
    {
        
        ImagePin.ImagePin ocrResultShowWindow =null;
        ct.Register(() =>
        {
            Dispatcher.UIThread.InvokeAsync((() =>
            {
                 ocrResultShowWindow.Close();
            }));
           
        });
        Dispatcher.UIThread.Invoke((() =>
        {
            ocrResultShowWindow = new ImagePin.ImagePin();
            ocrResultShowWindow.Show();
            
        }));
        return ocrResultShowWindow; 
    }
    [ScenarioMethod("设置置顶窗口图片",$"return=图片置顶窗口实例")]
    public void SetImagePin(ImagePin.ImagePin imagePin, ScreenCaptureResult screenCapture, CancellationToken ct)
    {
        if (imagePin == null) return;
        Dispatcher.UIThread.Invoke((() =>
        {
            if (imagePin.Image.Source is null  )
            {
                imagePin.Image.Source =screenCapture.Source.ToAWriteableBitmap();
            }
            else if (imagePin.Image.Source is WriteableBitmap writeableBitmap)
            {
                if (writeableBitmap.Size.Width!= screenCapture.Source.Width ||
                    writeableBitmap.Size.Height!= screenCapture.Source.Height)
                {
                    imagePin.Image.Source =screenCapture.Source.ToAWriteableBitmap();
                }
                else
                {
                    if (!screenCapture.Source.IsContinuous())
                    {
                        screenCapture.Source= screenCapture.Source.Clone();
                    }
                    using (var l = writeableBitmap.Lock())
                    {
                        unsafe
                        {
                            var destinationSizeInBytes = screenCapture.Source.Width * 4 * screenCapture.Source.Height;

                            Buffer.MemoryCopy(screenCapture.Source.DataPointer, (void*)l.Address,
                                destinationSizeInBytes, destinationSizeInBytes);


                        }
                    }
                    imagePin.Image.InvalidateVisual();
                }
                
            }

            
        }));
    }
    
}