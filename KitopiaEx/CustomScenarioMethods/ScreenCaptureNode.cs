using PluginCore.Localization;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KitopiaEx.INodeInputConnector.ScreenCaptureInfoSelfConnector;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using PluginCore;
using PluginCore.CustomScenario.Attribute.Scenario;

namespace KitopiaEx.CustomScenarioMethods;

[ScenarioMethodCategory("lang.kitopiaex.screenshot")]
public class ScreenCaptureNode
{
    [ScenarioMethod("lang.kitopiaex.select_capture_region", "screenCaptureInfoSelf=lang.kitopiaex.capture_information", "return=lang.kitopiaex.capture_region_information", Id = "选定截图区域")]
    public ScreenCaptureInfo SelectTheScreenshotArea(
        [SelfInput] [CustomNodeInputType(typeof(ScreenCaptureInfoSelfConnector))]
        ScreenCaptureInfo screenCaptureInfoSelf, CancellationToken ct)
    {
        return screenCaptureInfoSelf;
    }
    [ScenarioMethod("lang.kitopiaex.get_selected_region_capture_information", "screenCaptureInfoSelf=lang.kitopiaex.capture_information", "return=lang.kitopiaex.screenshot", Id = "获取指定区域截图信息")]
    public ScreenCaptureResult ScreenshotTheSelectArea(
        [CustomNodeInputType(typeof(ScreenCaptureInfoSelfConnector))]
        ScreenCaptureInfo screenCaptureInfoSelf, CancellationToken ct)
    {
        return ServiceManager.Services.GetService<IScreenCaptureManager>().CaptureScreenBytes(screenCaptureInfoSelf);

    }
    [ScenarioMethod("lang.kitopiaex.get_selected_region_image", "return=lang.kitopiaex.screenshot", Id = "获取指定区域截图数据")]
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
            throw new Exception(Lang.Get("lang.kitopiaex.screen_capture_was_canceled"));
        }
        return screenCaptureResult.Value;

    }
    [ScenarioMethod("lang.kitopiaex.save_image_to_file", "captureResult=lang.kitopiaex.screenshot", "return=lang.kitopiaex.screenshot", Id = "保存图片到文件")]
    public void SaveImage(
        ScreenCaptureResult captureResult, CancellationToken ct)
    {
        if (captureResult.Source is not null)
        {
            var ts = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0);
            var timeStamp = Convert.ToInt64(ts.TotalMilliseconds);
            var f = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads\\Kitopia" +
                    timeStamp + ".png";
            if (!Cv2.ImWrite(f, captureResult.Source))
            {
                throw new IOException(Lang.Format("lang.kitopiaex.messages.unable_to_save_the_capture_to_value", f));
            }
        }
        else
        {
            throw new Exception(Lang.Get("lang.kitopiaex.no_image_data"));
        }

    }
}
