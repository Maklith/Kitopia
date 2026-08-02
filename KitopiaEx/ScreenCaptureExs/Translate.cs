using System.Threading;
using KitopiaEx.Translate;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.CustomScenario.Attribute;

namespace KitopiaEx.ScreenCaptureExs;

/// <summary>
/// 翻译功能类，提供屏幕截图的OCR和翻译功能
/// Translation class that provides OCR and translation functionality for screen captures
/// </summary>
public class Translate
{
    /// <summary>
    /// 翻译图像捕获结果，对截图进行OCR识别并翻译文本
    /// Translate image capture result by performing OCR and translating the text
    /// </summary>
    /// <param name="dResult">屏幕截图结果 / Screen capture result</param>
    [Feature(
        "translate",
        "翻译",
        "选择屏幕区域，识别其中的文字并翻译为简体中文。",
        "截图与图像",
        0xf834,
        120,
        Activation = FeatureActivationMode.ScreenCapture)]
    [Capture("翻译", 0xf834)]
    public void TranslateImgCapture(ScreenCaptureResult dResult)
    {
        var service = KitopiaEx.ServiceProvider.GetService<global::KitopiaEx.CustomScenarioMethods.Ocr>()!;
        var ocrResults = service.OcrImg(dResult, CancellationToken.None);
        var service2 = KitopiaEx.ServiceProvider.GetService<global::KitopiaEx.CustomScenarioMethods.Translate>()!;
        ocrResults = service2.TranslateOcrResults(ocrResults, SourceTranslateLang.自动检测, TargetTranslateLang.简体中文,
            CancellationToken.None).GetAwaiter().GetResult();
        service.OcrResultShow(dResult, ocrResults, CancellationToken.None);
    }
}
