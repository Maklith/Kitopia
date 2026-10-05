// Author: liaom
// SolutionName: Kitopia
// ProjectName: KitopiaEx
// FileName:Image.cs
// Date: 2025/12/29 10:12
// FileEffect:

using PluginCore.Localization;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.CustomScenario.Attribute.Scenario;

namespace KitopiaEx.CustomScenarioMethods;

public class Image
{
    [ScenarioMethod("lang.kitopiaex.save_image_to_selected_location", $"{nameof(captureResult)}=lang.kitopiaex.image_data", $"{nameof(path)}=lang.kitopiaex.save_path", Id = "保存图片到指定位置")]
    public void SaveImageToPath(ScreenCaptureResult captureResult, string path,CancellationToken ct)
    {
        if (captureResult.Source == null)
        {
            throw new System.Exception(Lang.Get("lang.kitopiaex.cannot_save_an_empty_image"));
        }
        var imageTool = Kitopia.ServiceProvider.GetService<IImageTool>()!;
        imageTool.SaveImageAndOpenTheFolder(captureResult.Source, path);
    }
    [ScenarioMethod("lang.kitopiaex.save_image_and_open_containing_folder", $"{nameof(captureResult)}=lang.kitopiaex.image_data", $"{nameof(path)}=lang.kitopiaex.save_path", Id = "保存图片到指定位置并打开保存目录")]
    public void SaveImageToPathAndOpenFolder(ScreenCaptureResult captureResult, string path,CancellationToken ct)
    {
        if (captureResult.Source == null)
        {
            throw new System.Exception(Lang.Get("lang.kitopiaex.cannot_save_an_empty_image"));
        }
        var imageTool = Kitopia.ServiceProvider.GetService<IImageTool>()!;
        imageTool.SaveImageAndOpenTheFolder(captureResult.Source, path);
    }

    [ScenarioMethod("lang.kitopiaex.copy_image_to_clipboard", $"{nameof(captureResult)}=lang.kitopiaex.image_data", Id = "复制图片到剪贴板")]
    public void CopyImageToClipboard(ScreenCaptureResult captureResult,CancellationToken ct)
    {
        
        if (captureResult.Source == null)
        {
            throw new System.Exception(Lang.Get("lang.kitopiaex.cannot_copy_an_empty_image_to_the_clipboard"));
        }
        var clipboardService = Kitopia.ServiceProvider.GetService<IClipboardService>()!;
        if (!clipboardService.SetImageAsync(captureResult).GetAwaiter().GetResult())
        {
            throw new System.InvalidOperationException(Lang.Get("lang.kitopiaex.unable_to_copy_the_image_to_the_clipboard"));
        }
    }
}
