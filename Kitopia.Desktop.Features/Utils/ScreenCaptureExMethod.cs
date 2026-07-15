using PluginCore;

namespace Kitopia.Desktop.Features.Utils;

public class ScreenCaptureExMethod
{
    public Action<ScreenCaptureResult> Action { get; set; }
    public string Description { get; set; }
    public int Symbol { get; set; }
}