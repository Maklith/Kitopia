namespace Kitopia.Desktop.Features.Ocr;

/// <summary>
/// Main-program marker for the shared host OCR service.
/// </summary>
public interface IOcrService : PluginCore.IOcrService
{
    Task ReleaseSessionsAsync();
}
