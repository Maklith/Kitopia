using System.Threading;
using KitopiaEx.Translate;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Attribute;

namespace KitopiaEx.ScreenCaptureExs;

public class Translate
{
    [Capture("翻译", 0xf834)]
    public void TranslateImgCapture(ScreenCaptureResult dResult)
    {
        var service = KitopiaEx.ServiceProvider.GetService<global::KitopiaEx.Ocr.Ocr>()!;
        var ocrResults = service.OcrImg(dResult, CancellationToken.None);
        var service2 = KitopiaEx.ServiceProvider.GetService<global::KitopiaEx.Translate.Translate>()!;
        ocrResults = service2.TranslateOcrResults(ocrResults, SourceTranslateLang.自动检测, TargetTranslateLang.简体中文,
            CancellationToken.None).GetAwaiter().GetResult();
        service.OcrResultShow(dResult, ocrResults, CancellationToken.None);
    }
}