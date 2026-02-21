// Author: liaom
// SolutionName: Kitopia
// ProjectName: KitopiaEx
// FileName:Image.cs
// Date: 2025/12/29 10:12
// FileEffect:

using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.CustomScenario.Attribute.Scenario;

namespace KitopiaEx.CustomScenarioMethods;

public class Image
{
    [ScenarioMethod("保存图片到指定位置", $"{nameof(captureResult)}=图像数据", $"{nameof(path)}=保存路径")]
    public void SaveImageToPath(ScreenCaptureResult captureResult, string path,CancellationToken ct)
    {
        if (captureResult.Source == null)
        {
            throw new System.Exception("图像数据为空，无法保存。");
        }
        var imageTool = Kitopia.ServiceProvider.GetService<IImageTool>()!;
        imageTool.SaveImageAndOpenTheFolder(captureResult.Source, path);
    }
    [ScenarioMethod("保存图片到指定位置并打开保存目录", $"{nameof(captureResult)}=图像数据", $"{nameof(path)}=保存路径")]
    public void SaveImageToPathAndOpenFolder(ScreenCaptureResult captureResult, string path,CancellationToken ct)
    {
        if (captureResult.Source == null)
        {
            throw new System.Exception("图像数据为空，无法保存。");
        }
        var imageTool = Kitopia.ServiceProvider.GetService<IImageTool>()!;
        imageTool.SaveImageAndOpenTheFolder(captureResult.Source, path);
    }

    [ScenarioMethod("复制图片到剪贴板", $"{nameof(captureResult)}=图像数据")]
    public void CopyImageToClipboard(ScreenCaptureResult captureResult,CancellationToken ct)
    {
        
        if (captureResult.Source == null)
        {
            throw new System.Exception("图像数据为空，无法复制到剪贴板。");
        }
        var clipboardService = Kitopia.ServiceProvider.GetService<IClipboardService>()!;
        clipboardService.SetImageAsync(captureResult).GetAwaiter().GetResult();
    }
}