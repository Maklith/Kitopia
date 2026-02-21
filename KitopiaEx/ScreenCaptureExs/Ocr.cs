using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.CustomScenario.Attribute;

namespace KitopiaEx.ScreenCaptureExs;

public class Ocr
{
    [Capture("文字识别",0xEA72)]
    public void OcrImgCapture(ScreenCaptureResult dResult)
    {
        var service = KitopiaEx.ServiceProvider.GetService<global::KitopiaEx.CustomScenarioMethods.Ocr>();
        var ocrResults = service!.OcrImg(dResult, CancellationToken.None);
        service.OcrResultShow(dResult, ocrResults, CancellationToken.None);
    }
}