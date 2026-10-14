// Author: liaom
// SolutionName: Kitopia
// ProjectName: KitopiaEx
// FileName:Image.cs
// Date: 2025/12/29 10:12
// FileEffect:

using PluginCore.Localization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.CustomScenario.Attribute.Scenario;

namespace KitopiaEx.CustomScenarioMethods;

[ScenarioMethodCategory("lang.kitopiaex.image")]
public class Image
{
    [ScenarioMethod("lang.kitopiaex.save_image_to_selected_location", $"{nameof(captureResult)}=lang.kitopiaex.image_data", $"{nameof(path)}=lang.kitopiaex.save_path", "return=lang.kitopiaex.saved_path", Id = "保存图片到指定位置")]
    public string SaveImageToPath(ScreenCaptureResult captureResult, [SelfInput] string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        System.ArgumentException.ThrowIfNullOrWhiteSpace(path);
        path = Path.GetFullPath(path);
        if (captureResult.Source == null)
        {
            throw new System.Exception(Lang.Get("lang.kitopiaex.cannot_save_an_empty_image"));
        }
        var imageTool = Kitopia.ServiceProvider.GetRequiredService<IImageTool>();
        if (!imageTool.SaveImage(captureResult.Source, path))
            throw new IOException(Lang.Format("lang.kitopiaex.messages.unable_to_save_the_capture_to_value", path));
        return path;
    }
    [ScenarioMethod("lang.kitopiaex.save_image_and_open_containing_folder", $"{nameof(captureResult)}=lang.kitopiaex.image_data", $"{nameof(path)}=lang.kitopiaex.save_path", "return=lang.kitopiaex.saved_path", Id = "保存图片到指定位置并打开保存目录")]
    public string SaveImageToPathAndOpenFolder(ScreenCaptureResult captureResult, [SelfInput] string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        System.ArgumentException.ThrowIfNullOrWhiteSpace(path);
        path = Path.GetFullPath(path);
        if (captureResult.Source == null)
        {
            throw new System.Exception(Lang.Get("lang.kitopiaex.cannot_save_an_empty_image"));
        }
        var imageTool = Kitopia.ServiceProvider.GetRequiredService<IImageTool>();
        if (!imageTool.SaveImageAndOpenTheFolder(captureResult.Source, path))
            throw new IOException(Lang.Format("lang.kitopiaex.messages.unable_to_save_the_capture_to_value", path));
        return path;
    }

    [ScenarioMethod("lang.kitopiaex.copy_image_to_clipboard", $"{nameof(captureResult)}=lang.kitopiaex.image_data", Id = "复制图片到剪贴板")]
    public async Task CopyImageToClipboard(ScreenCaptureResult captureResult,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (captureResult.Source == null)
        {
            throw new System.Exception(Lang.Get("lang.kitopiaex.cannot_copy_an_empty_image_to_the_clipboard"));
        }
        var clipboardService = Kitopia.ServiceProvider.GetRequiredService<IClipboardService>();
        if (!await clipboardService.SetImageAsync(captureResult).WaitAsync(ct).ConfigureAwait(false))
        {
            throw new System.InvalidOperationException(Lang.Get("lang.kitopiaex.unable_to_copy_the_image_to_the_clipboard"));
        }
    }
}
