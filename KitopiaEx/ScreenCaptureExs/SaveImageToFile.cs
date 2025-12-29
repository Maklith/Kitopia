using System;
using Core.Services;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Attribute;

namespace KitopiaEx.ScreenCaptureExs;

public class SaveImageToFile
{
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