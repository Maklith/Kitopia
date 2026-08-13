using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using KitopiaEx.Ocr;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using PluginCore;
using PluginCore.CustomScenario.Attribute;
using PluginCore.CustomScenario.Attribute.Scenario;
using PluginCore.ExMethod;
using Point = Avalonia.Point;

namespace KitopiaEx.CustomScenarioMethods;

[ScenarioMethodCategory("文字识别")]
public class Ocr
{
    [ScenarioMethod("文字提取", $"{nameof(dResult)}=截图数据", "return=文字识别结果数据")]
    public IEnumerable<OcrResult> OcrImg(ScreenCaptureResult dResult, CancellationToken ct)
    {
        if (dResult.Source is null)
        {
            throw new Exception("无图像数据");
        }

        return OcrImgBase(dResult.Source, ct);
    }

    internal IEnumerable<OcrResult> OcrImgBase(Mat image, CancellationToken ct)
    {
        var service = Kitopia.ServiceProvider.GetService<IOcrService>();
        if (service is null || !service.IsAvailable)
        {
            throw new InvalidOperationException("主程序本地 OCR 模型不可用。");
        }

        return service.RecognizeAsync(image, ct).GetAwaiter().GetResult()
            .Select(region => new OcrResult
            {
                SPoint = new Point(region.Left, region.Top),
                EPoint = new Point(region.Left + region.Width, region.Top + region.Height),
                Text = region.Text
            })
            .ToArray();
    }

    [ScenarioMethod("文字提取结果显示", $"{nameof(dResult)}=截图数据", $"{nameof(ocrResults)}=文字识别结果数据")]
    public void OcrResultShow(ScreenCaptureResult dResult, IEnumerable<OcrResult> ocrResults, CancellationToken ct)
    {
        if (dResult.Source != null) OcrResultShowBase(dResult.Source, ocrResults, ct);
    }

    internal void OcrResultShowBase(Mat img, IEnumerable<OcrResult> ocrResults, CancellationToken ct)
    {
        Dispatcher.UIThread.Invoke((() =>
        {
            var ocrResultShowWindow = new OcrResultShowWindow();
            ocrResultShowWindow.Image.Source = img.ToAWriteableBitmap();
            ocrResultShowWindow.ItemsControl.ItemsSource = ocrResults;
            ocrResultShowWindow.Show();
        }));
    }

    [ScenarioMethod("获取文字提取结果显示实例", "return=文字提取结果显示实例")]
    public OcrResultShowWindow OcrResultShowIn(CancellationToken ct)
    {
        OcrResultShowWindow ocrResultShowWindow = null;
        ct.Register(() =>
        {
            Dispatcher.UIThread.InvokeAsync((() => ocrResultShowWindow.Close()));
        });
        Dispatcher.UIThread.Invoke((() =>
        {
            ocrResultShowWindow = new OcrResultShowWindow();
            ocrResultShowWindow.Show();
        }));
        return ocrResultShowWindow;
    }

    [ScenarioMethod("设置文字提取结果", $"{nameof(screenCapture)}=截图数据", $"{nameof(ocrResults)}=文字识别结果数据")]
    public void SetOcrResultShowWindowData(OcrResultShowWindow imagePin, ScreenCaptureResult screenCapture,
        IEnumerable<OcrResult> ocrResults, CancellationToken ct)
    {
        if (imagePin == null) return;
        Dispatcher.UIThread.Invoke((() =>
        {
            if (imagePin.Image.Source is null)
            {
                imagePin.Image.Source = screenCapture.Source.ToAWriteableBitmap();
            }
            else if (imagePin.Image.Source is WriteableBitmap writeableBitmap)
            {
                if (writeableBitmap.Size.Width != screenCapture.Source.Width ||
                    writeableBitmap.Size.Height != screenCapture.Source.Height)
                {
                    imagePin.Image.Source = screenCapture.Source.ToAWriteableBitmap();
                }
                else
                {
                    if (!screenCapture.Source.IsContinuous())
                    {
                        screenCapture.Source = screenCapture.Source.Clone();
                    }

                    using var l = writeableBitmap.Lock();
                    unsafe
                    {
                        var destinationSizeInBytes = screenCapture.Source.Width * 4 * screenCapture.Source.Height;
                        Buffer.MemoryCopy(screenCapture.Source.DataPointer, (void*)l.Address,
                            destinationSizeInBytes, destinationSizeInBytes);
                    }
                }
            }

            imagePin.ItemsControl.ItemsSource = ocrResults;
            imagePin.UpdateImageScale();
        }));
    }
}
