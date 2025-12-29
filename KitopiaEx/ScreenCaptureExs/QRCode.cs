using System.Threading;
using KitopiaEx.QRCode;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Attribute;

namespace KitopiaEx.ScreenCaptureExs;

public class QRCode
{
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