using Core.Window.ScreenCapture;
using PluginCore;

namespace KitopiaTest;

[TestClass]
public sealed class ScreenCaptureWindowInfoTests
{
    [TestMethod]
    public void GetVisibleWindowRectForSelection_UsesExtendedFrameBoundsWhenAvailable()
    {
        var getWindowRect = new Rect(92, 100, 416, 308);
        var extendedFrameRect = new Rect(100, 100, 400, 300);
        var screenRect = new Rect(0, 0, 1920, 1080);

        var result = ScreenCaptureInfoEx.GetVisibleWindowRectForSelection(getWindowRect, extendedFrameRect, screenRect);

        Assert.AreEqual(extendedFrameRect, result);
    }

    [TestMethod]
    public void GetVisibleWindowRectForSelection_FallsBackToGetWindowRectWhenExtendedFrameMissing()
    {
        var getWindowRect = new Rect(92, 100, 416, 308);
        var screenRect = new Rect(0, 0, 1920, 1080);

        var result = ScreenCaptureInfoEx.GetVisibleWindowRectForSelection(getWindowRect, null, screenRect);

        Assert.AreEqual(getWindowRect, result);
    }
}
