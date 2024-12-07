using System.Threading;
using System.Threading.Tasks;
using KitopiaEx.INodeInputConnector.ScreenCaptureInfoSelfConnector;
using PluginCore;
using PluginCore.Attribute;
using PluginCore.Attribute.Scenario;
using SharpHook.Native;

namespace KitopiaEx;

[ScenarioMethodCategory("截图")]
public class ScreenCaptureNode
{
    [ScenarioMethod("选定截图区域", "screenCaptureInfoSelf=截图信息", "return=截图区域信息")]
    public ScreenCaptureInfo SelectTheScreenshotArea(
        [SelfInput] [CustomNodeInputType(typeof(ScreenCaptureInfoSelfConnector))]
        ScreenCaptureInfo screenCaptureInfoSelf, CancellationToken ct)
    {
        return screenCaptureInfoSelf;
    }
}