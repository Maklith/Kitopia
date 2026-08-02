using System;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.CustomScenario.Attribute;

namespace KitopiaEx.ScreenCaptureExs;

public class SaveImageToFile
{
    [Feature(
        "save-captured-image",
        "保存图像到本地",
        "选择屏幕区域，将截图保存到下载目录并打开所在位置。",
        "截图与图像",
        0xE357,
        150,
        Activation = FeatureActivationMode.ScreenCapture)]
    [Capture("保存图像到本地",0xE357)]
    public void SaveImageToFileM(ScreenCaptureResult dResult)
    {
        var ts = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0);
        var timeStamp = Convert.ToInt64(ts.TotalMilliseconds);
        var f = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) +
                "\\Downloads\\Kitopia" +
                timeStamp + ".png";
        var imageTool = Kitopia.ServiceProvider.GetService<IImageTool>()!;
        if (dResult.Source == null)
        {
            return;
        }
        imageTool.SaveImageAndOpenTheFolder(dResult.Source, f);
        
        return;
    }
}
