using OpenCvSharp;

namespace Core.Services;

/// <summary>
/// 图像工具服务接口 / Image tool service interface for image operations
/// </summary>
public interface IImageTool
{
    /// <summary>
    /// 保存图像并打开文件夹 / Save image and open the containing folder
    /// </summary>
    /// <param name="image">要保存的图像 / The image to save</param>
    /// <param name="filePath">文件路径 / The file path (optional)</param>
    /// <returns>是否成功 / Whether the operation succeeded</returns>
    public bool SaveImageAndOpenTheFolder(Mat image, string? filePath = null);
    
    /// <summary>
    /// 保存图像 / Save image to file
    /// </summary>
    /// <param name="image">要保存的图像 / The image to save</param>
    /// <param name="filePath">文件路径 / The file path (optional)</param>
    /// <returns>是否成功 / Whether the operation succeeded</returns>
    public bool SaveImage(Mat image, string? filePath = null);
}