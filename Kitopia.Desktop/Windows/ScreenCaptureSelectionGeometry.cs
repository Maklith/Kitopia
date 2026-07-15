using Avalonia;
using Math = System.Math;

namespace Kitopia.Desktop.Windows;

public static class ScreenCaptureSelectionGeometry
{
    public const double SelectionChromeInset = 4d;

    public static Rect GetDisplayRectForContentRect(Rect contentRect)
    {
        return new Rect(
            contentRect.X - SelectionChromeInset,
            contentRect.Y - SelectionChromeInset,
            contentRect.Width + SelectionChromeInset * 2,
            contentRect.Height + SelectionChromeInset * 2);
    }

    public static Rect GetContentRectForDisplayRect(Rect displayRect)
    {
        var width = Math.Max(0d, displayRect.Width - SelectionChromeInset * 2);
        var height = Math.Max(0d, displayRect.Height - SelectionChromeInset * 2);
        return new Rect(displayRect.X + SelectionChromeInset, displayRect.Y + SelectionChromeInset, width, height);
    }
}
