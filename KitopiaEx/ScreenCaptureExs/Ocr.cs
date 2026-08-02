using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.CustomScenario.Attribute;

namespace KitopiaEx.ScreenCaptureExs;

public class Ocr
{
    [Feature(
        "ocr",
        "文字识别",
        "选择屏幕区域，识别图像中的文字并显示结果。",
        "截图与图像",
        0xEA72,
        110,
        Activation = FeatureActivationMode.ScreenCapture)]
    [Capture("文字识别",0xEA72)]
    public void OcrImgCapture(ScreenCaptureResult dResult)
    {
        var service = KitopiaEx.ServiceProvider.GetService<global::KitopiaEx.CustomScenarioMethods.Ocr>();
        var ocrResults = service!.OcrImg(dResult, CancellationToken.None);
        service.OcrResultShow(dResult, ocrResults, CancellationToken.None);
    }
}
