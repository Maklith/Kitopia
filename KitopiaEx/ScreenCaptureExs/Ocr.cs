using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Attribute;

namespace KitopiaEx.ScreenCaptureExs;

public class Ocr
{
    [Capture("文字识别",0xEA72)]
    public void OcrImgCapture(ScreenCaptureResult dResult)
    {
        var service = KitopiaEx.ServiceProvider.GetService<global::KitopiaEx.Ocr.Ocr>();
        var ocrResults = service!.OcrImg(dResult, CancellationToken.None);
        service.OcrResultShow(dResult, ocrResults, CancellationToken.None);
    }
}