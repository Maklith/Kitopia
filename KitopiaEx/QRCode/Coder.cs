using System.Threading;
using KitopiaEx.Translate;
using OpenCvSharp;
using PluginCore;
using PluginCore.Attribute;

namespace KitopiaEx.QRCode;

public class Coder
{
    [ScenarioMethod("识别QRCode", $"{nameof(captureResult)}=图像数据","return=QRCode识别结果")]
    public string QRCodeDecode(ScreenCaptureResult captureResult, CancellationToken ct)
    {
        var mat=Mat.FromPixelData(captureResult.Info.Height,captureResult.Info.Width,MatType.CV_8UC4,captureResult.Bytes);
        var qrCodeDetector = new QRCodeDetector();
        var detectAndDecode = qrCodeDetector.DetectAndDecode(mat, out var result);
        if (result.Length==0)
        {
            return string.Empty;
        }
        return detectAndDecode;
    }
    [Capture("识别二维码",0xf635)]
    public void QRCodeImgCapture(ScreenCaptureResult dResult)
    {
        var qrCodeDecode = QRCodeDecode(dResult,CancellationToken.None);
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