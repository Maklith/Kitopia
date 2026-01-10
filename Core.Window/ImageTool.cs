using System;
using Core.SDKs.Services;
using Core.Services;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using PluginCore;

namespace Core.Window;

public class ImageTool : IImageTool
{
    public bool SaveImageAndOpenTheFolder(Mat image, string? filePath = null)
    {
        if (SaveImage(image,filePath))
        {
            ServiceManager.Services.GetService<IShellUtils>()!.OpenFolderAndSelect(filePath!);
        }

        return false;
    }

    public bool SaveImage(Mat image, string? filePath = null)
    {
        try
        {
            image.SaveImage(filePath);
            return true;
        }
        catch (Exception e)
        {
            return false;
        }
       
    }
}