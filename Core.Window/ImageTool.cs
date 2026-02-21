using Core.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using PluginCore;

namespace Core.Window;

public class ImageTool : IImageTool
{
    public bool SaveImageAndOpenTheFolder(Mat image, string filePath)
    {
        if (SaveImage(image,filePath))
        {
            ServiceManager.Services.GetService<IShellUtils>()!.OpenFolderAndSelect(filePath);
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