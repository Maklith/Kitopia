using System;
using System.Threading;
using System.Threading.Tasks;
using Core.SDKs.Services;
using KitopiaEx.INodeInputConnector.ScreenCaptureInfoSelfConnector;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Attribute;
using PluginCore.Attribute.Scenario;
using SharpHook.Native;

namespace KitopiaEx;

[ScenarioMethodCategory("截图")]
public class ScreenCaptureNode
{
    [ScenarioMethod("选定截图区域", "screenCaptureInfoSelf=截图信息", "return=截图区域信息")]
    public ScreenCaptureInfo SelectTheScreenshotArea(
        [SelfInput] [CustomNodeInputType(typeof(ScreenCaptureInfoSelfConnector))]
        ScreenCaptureInfo screenCaptureInfoSelf, CancellationToken ct)
    {
        return screenCaptureInfoSelf;
    }
    [ScenarioMethod("获取指定区域截图", "screenCaptureInfoSelf=截图信息", "return=截图")]
    public ScreenCaptureResult ScreenshotTheSelectArea(
        [CustomNodeInputType(typeof(ScreenCaptureInfoSelfConnector))]
        ScreenCaptureInfo screenCaptureInfoSelf, CancellationToken ct)
    {
        return ServiceManager.Services.GetService<IScreenCaptureManager>().CaptureScreenBytes(screenCaptureInfoSelf);

    }
    [ScenarioMethod("保存图片到文件", "captureResult=截图", "return=截图")]
    public void SaveImage(
        ScreenCaptureResult captureResult, CancellationToken ct)
    {
        if (captureResult.Source is null && captureResult.Bytes is not null)
        {
            var captureScreenBitmap = ServiceManager.Services.GetService<IScreenCaptureManager>().CaptureScreenBitmap(captureResult);
            var ts = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0);
            var timeStamp = Convert.ToInt64(ts.TotalMilliseconds);
            var f = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads\\Kitopia" +
                    timeStamp + ".png";
            captureScreenBitmap.Source.Save(f);
            captureScreenBitmap.Source.Dispose();
        }else if (captureResult.Source is not null)
        {
            var ts = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0);
            var timeStamp = Convert.ToInt64(ts.TotalMilliseconds);
            var f = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads\\Kitopia" +
                    timeStamp + ".png";
            captureResult.Source.Save(f);
        }
        else
        {
            throw new Exception("无图像数据");
        }

    }
}