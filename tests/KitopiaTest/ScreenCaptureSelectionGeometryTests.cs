using Avalonia;
using Kitopia.Desktop.Windows;

namespace KitopiaTest;

[TestClass]
public sealed class ScreenCaptureSelectionGeometryTests
{
    [TestMethod]
    public void GetDisplayRectForContentRect_ExpandsByChromeInset()
    {
        var contentRect = new Rect(100, 50, 300, 200);

        var displayRect = ScreenCaptureSelectionGeometry.GetDisplayRectForContentRect(contentRect);

        Assert.AreEqual(new Rect(96, 46, 308, 208), displayRect);
    }

    [TestMethod]
    public void GetContentRectForDisplayRect_ReversesExpandedDisplayRect()
    {
        var displayRect = new Rect(96, 46, 308, 208);

        var contentRect = ScreenCaptureSelectionGeometry.GetContentRectForDisplayRect(displayRect);

        Assert.AreEqual(new Rect(100, 50, 300, 200), contentRect);
    }
}
