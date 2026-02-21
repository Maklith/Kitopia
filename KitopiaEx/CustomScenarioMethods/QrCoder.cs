using System.Threading;
using OpenCvSharp;
using PluginCore;
using PluginCore.CustomScenario.Attribute.Scenario;

namespace KitopiaEx.CustomScenarioMethods;

public class QrCoder
{
    [ScenarioMethod("识别QRCode", $"{nameof(captureResult)}=图像数据","return=QRCode识别结果")]
    public string QRCodeDecode(ScreenCaptureResult captureResult, CancellationToken ct)
    {
        var qrCodeDetector = new QRCodeDetector();
        if (captureResult.Source == null)
        {
            return string.Empty;
        }

        var detectAndDecode = qrCodeDetector.DetectAndDecode(captureResult.Source, out var result);
        if (result.Length == 0)
        {
            return string.Empty;
        }

        return detectAndDecode;
    }
    
}