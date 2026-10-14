using System.Threading;
using OpenCvSharp;
using PluginCore;
using PluginCore.CustomScenario.Attribute.Scenario;

namespace KitopiaEx.CustomScenarioMethods;

public class QrCoder
{
    [ScenarioMethod("lang.kitopiaex.decode_qr_code", $"{nameof(captureResult)}=lang.kitopiaex.image_data","return=lang.kitopiaex.qr_code_result", Id = "识别QRCode")]
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