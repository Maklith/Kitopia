using System;
using System.IO;
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
        if (dResult.Source == null)
        {
            return;
        }
        Kitopia.ServiceProvider.GetRequiredService<IImageTool>()
            .SaveImageAndOpenTheFolder(dResult.Source, GetSavePath());
    }

    internal static string GetSavePath()
    {
        var directory = Config.INSTANCE?.ImageSaveDirectory;
        if (string.IsNullOrWhiteSpace(directory))
            directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"Kitopia{Guid.NewGuid():N}.png");
    }
}
