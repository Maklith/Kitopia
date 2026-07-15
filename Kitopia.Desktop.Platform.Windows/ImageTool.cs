using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Abstractions.Shell;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using PluginCore;

namespace Kitopia.Desktop.Platform.Windows;

public class ImageTool : IImageTool
{
    public bool SaveImageAndOpenTheFolder(Mat image, string filePath)
    {
        if (SaveImage(image,filePath))
        {
            ServiceManager.Services.GetService<IDesktopShell>()!.OpenFolderAndSelect(filePath);
        }

        return false;
    }

    public bool SaveImage(Mat image, string filePath)
    {
        try
        {
            image.SaveImage(filePath);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
       
    }
}
