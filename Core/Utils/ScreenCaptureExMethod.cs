using PluginCore;

namespace Core.SDKs;

public class ScreenCaptureExMethod
{
    public Action<ScreenCaptureResult> Action{ get; set; }
    public string Description{ get; set; }
    public int Symbol { get; set; }
}