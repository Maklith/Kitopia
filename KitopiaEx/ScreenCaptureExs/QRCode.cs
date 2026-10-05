using PluginCore.Localization;
using System.Threading;
using KitopiaEx.CustomScenarioMethods;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.CustomScenario.Attribute;

namespace KitopiaEx.ScreenCaptureExs;

public class QRCode
{
    [Feature(
        "qr-code",
        "lang.kitopiaex.read_qr_code",
        "lang.kitopiaex.select_a_screen_region_read_its_qr_code_and_copy_the_content",
        "lang.kitopia.screenshots_and_images",
        0xf635,
        130,
        Activation = FeatureActivationMode.ScreenCapture)]
    [Capture("lang.kitopiaex.read_qr_code",0xf635)]
    public void QRCodeImgCapture(ScreenCaptureResult dResult)
    {
        var service = KitopiaEx.ServiceProvider.GetService<QrCoder>();
        var qrCodeDecode = service.QRCodeDecode(dResult,CancellationToken.None);
        if (qrCodeDecode==string.Empty)
        {
            Kitopia.IToastService.Show("QRCode",Lang.Get("lang.kitopiaex.no_unique_qr_code_was_detected"));
            return;
        }
        Kitopia.IToastService.Show("QRCode",Lang.Format("lang.kitopiaex.messages.copied_to_clipboard_value", qrCodeDecode));
        Kitopia.IClipboardService.SetText(qrCodeDecode);
        
        return;
    }
}
