using PluginCore.Localization;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using KitopiaEx.INodeInputConnector.ScreenCaptureInfoSelfConnector;
using Microsoft.Extensions.DependencyInjection;
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
        ct.ThrowIfCancellationRequested();
        return screenCaptureInfoSelf;
    }
    [ScenarioMethod("lang.kitopiaex.get_selected_region_capture_information", "screenCaptureInfoSelf=lang.kitopiaex.capture_information", "return=lang.kitopiaex.screenshot", Id = "获取指定区域截图信息")]
    public ScreenCaptureResult ScreenshotTheSelectArea(
        [CustomNodeInputType(typeof(ScreenCaptureInfoSelfConnector))]
        ScreenCaptureInfo screenCaptureInfoSelf, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return ServiceManager.Services.GetRequiredService<IScreenCaptureManager>().CaptureScreenBytes(screenCaptureInfoSelf);

    }
    [ScenarioMethod("lang.kitopiaex.get_selected_region_image", "return=lang.kitopiaex.screenshot", Id = "获取指定区域截图数据")]
    public Task<ScreenCaptureResult> ScreenshotTheSelectArea(CancellationToken ct)
    {
        return ServiceManager.Services.GetRequiredService<IScreenCaptureWindow>().RequestUserSelectScreenBytesAsync(ct);
    }
    [ScenarioMethod("lang.kitopiaex.save_image_to_file", "captureResult=lang.kitopiaex.screenshot",
        "path=lang.kitopiaex.optional_save_path", "return=lang.kitopiaex.saved_path", Id = "保存图片到文件")]
    public string SaveImage(ScreenCaptureResult captureResult, [SelfInput] string? path = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (captureResult.Source is not null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                Directory.CreateDirectory(directory);
                path = Path.Combine(directory, $"Kitopia{Guid.NewGuid():N}.png");
            }
            path = Path.GetFullPath(path);
            if (!Kitopia.ServiceProvider.GetRequiredService<IImageTool>().SaveImage(captureResult.Source, path))
            {
                throw new IOException(Lang.Format("lang.kitopiaex.messages.unable_to_save_the_capture_to_value", path));
            }
            return path;
        }
        else
        {
            throw new Exception(Lang.Get("lang.kitopiaex.no_image_data"));
        }

    }
}
