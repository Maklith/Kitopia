using System;
using Core.SDKs.Services;
using Core.Services;
using OpenCvSharp;
using Vanara.PInvoke;

namespace Core.Window;

public class ImageTool : IImageTool
{
    public bool SaveImageAndOpenTheFolder(Mat image, string? filePath = null)
    {
        if (SaveImage(image,filePath))
        {
            Shell32.ShellExecute(IntPtr.Zero, "open", "explorer.exe", "/select," + filePath, "",
                ShowWindowCommand.SW_NORMAL);
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