using System;
using System.Threading;
using System.Threading.Tasks;
using Core.SDKs.Services;
using KitopiaEx.INodeInputConnector.ScreenCaptureInfoSelfConnector;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Attribute;
using PluginCore.Attribute.Scenario;

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
    [ScenarioMethod("获取指定区域截图信息", "screenCaptureInfoSelf=截图信息", "return=截图")]
    public ScreenCaptureResult ScreenshotTheSelectArea(
        [CustomNodeInputType(typeof(ScreenCaptureInfoSelfConnector))]
        ScreenCaptureInfo screenCaptureInfoSelf, CancellationToken ct)
    {
        return ServiceManager.Services.GetService<IScreenCaptureManager>().CaptureScreenBytes(screenCaptureInfoSelf);

    }
    [ScenarioMethod("获取指定区域截图数据", "return=截图")]
    public ScreenCaptureResult ScreenshotTheSelectArea(CancellationToken ct)
    {
        ScreenCaptureResult? screenCaptureResult = null;
        bool IsCancel = false;
        ServiceManager.Services.GetService<IScreenCaptureWindow>().RequestUserSelectScreenBytes((result =>
        {
            screenCaptureResult = result;
        }), () =>
        {
            IsCancel = true;
        });
            
        while (screenCaptureResult==null&& !IsCancel )
        {
            Task.Delay(100).GetAwaiter().GetResult();
        }

        if (IsCancel)
        {
            throw new Exception("用户取消截图");
        }
        return screenCaptureResult.Value;

    }
    [ScenarioMethod("保存图片到文件", "captureResult=截图", "return=截图")]
    public void SaveImage(
        ScreenCaptureResult captureResult, CancellationToken ct)
    {
        if (captureResult.Source is not null)
        {
            var ts = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0);
            var timeStamp = Convert.ToInt64(ts.TotalMilliseconds);
            var f = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads\\Kitopia" +
                    timeStamp + ".png";
            captureResult.Source.SaveImage(f);
        }
        else
        {
            throw new Exception("无图像数据");
        }

    }
}