using System.Threading;
using System.Threading.Tasks;
using PluginCore;
using PluginCore.Attribute;
using PluginCore.Attribute.Scenario;
using SharpHook.Native;

namespace KitopiaEx;
[ScenarioMethodCategory("截图")]
public class ScreenCaptureNode
{
    
    [ScenarioMethod("选定截图区域", "return=截图区域信息")]
    public ScreenCaptureInfo SelectTheScreenshotArea(CancellationToken ct)
    {
        return new ScreenCaptureInfo();
    }
}