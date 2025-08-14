using OpenCvSharp;

namespace Core.Services;

public interface IImageTool
{
    public bool SaveImageAndOpenTheFolder(Mat image, string? filePath = null);
    public bool SaveImage(Mat image, string? filePath = null);
}