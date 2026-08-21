using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Kitopia.Desktop.Ocr;

internal static class ControlHelper
{
    public static List<Control> GetControlsInBounds(Control root, Rect bounds)
    {
        var controls = new List<Control>();
        foreach (var visual in root.GetVisualDescendants())
        {
            if (visual is not Control control || control.GetPresentationSource()?.RootVisual is not { } rootVisual)
                continue;

            var controlBounds = control.Bounds.TransformToAABB(control.TransformToVisual(rootVisual).Value);
            if (bounds.Intersects(controlBounds))
                controls.Add(control);
        }

        return controls;
    }
}
