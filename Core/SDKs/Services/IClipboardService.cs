#region

using PluginCore;
using SixLabors.ImageSharp;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

#endregion

namespace Core.SDKs.Services;

public interface IClipboardService
{
    bool HasText();
    string GetText();
    bool SetText(string text);
    bool HasImage();
    Bitmap? GetImage();
    bool SetImage(Bitmap image);
    Task<bool> SetImageAsync(Image image);
    Task<bool> SetImageAsync(ScreenCaptureResult screenCaptureResult);
}

public enum ClipboardType
{
}