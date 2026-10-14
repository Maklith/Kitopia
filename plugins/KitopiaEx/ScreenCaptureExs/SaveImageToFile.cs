using System;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.CustomScenario.Attribute;

namespace KitopiaEx.ScreenCaptureExs;

public class SaveImageToFile
{
    [Feature(
        "save-captured-image",
        "lang.kitopiaex.save_image_locally",
        "lang.kitopiaex.select_a_screen_region_save_it_to_downloads_and_open_its_location",
        "lang.kitopia.screenshots_and_images",
        0xE357,
        150,
        Activation = FeatureActivationMode.ScreenCapture)]
    [Capture("lang.kitopiaex.save_image_locally",0xE357)]
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
