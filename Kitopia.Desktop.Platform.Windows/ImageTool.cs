using Kitopia.Desktop.Abstractions.Shell;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using PluginCore;

namespace Kitopia.Desktop.Platform.Windows;

public class ImageTool : IImageTool
{
    public bool SaveImageAndOpenTheFolder(Mat image, string filePath)
    {
        if (!SaveImage(image, filePath)) return false;
        ServiceManager.Services.GetRequiredService<IDesktopShell>().OpenFolderAndSelect(filePath);
        return true;
    }

    public bool SaveImage(Mat image, string filePath)
    {
        try
        {
            return Cv2.ImWrite(filePath, image);
        }
        catch (Exception)
        {
            return false;
        }
       
    }
}
