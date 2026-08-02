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
        "识别二维码",
        "选择屏幕区域，识别二维码并把内容复制到剪贴板。",
        "截图与图像",
        0xf635,
        130,
        Activation = FeatureActivationMode.ScreenCapture)]
    [Capture("识别二维码",0xf635)]
    public void QRCodeImgCapture(ScreenCaptureResult dResult)
    {
        var service = KitopiaEx.ServiceProvider.GetService<QrCoder>();
        var qrCodeDecode = service.QRCodeDecode(dResult,CancellationToken.None);
        if (qrCodeDecode==string.Empty)
        {
            Kitopia.IToastService.Show("QRCode","未检测到(检测到多个)二维码");
            return;
        }
        Kitopia.IToastService.Show("QRCode",$"已复制到剪贴板,内容:\n{qrCodeDecode}");
        Kitopia.IClipboardService.SetText(qrCodeDecode);
        
        return;
    }
}
