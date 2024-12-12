using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace KitopiaEx.Ocr;

public static class ControlHelper
{
    /// <summary>
    /// 检测某个控件是否在指定范围内。
    /// </summary>
    /// <param name="control">要检测的控件。</param>
    /// <param name="bounds">指定的范围（相对于根容器的坐标）。</param>
    /// <returns>如果控件在范围内，则返回 true；否则返回 false。</returns>
    public static bool IsControlInBounds(Control control, Rect bounds)
    {
        var visual = control as Control;
        if (visual == null)
            return false;

        // 获取控件的边界框（相对于根容器的坐标）。
        var controlBounds = visual.Bounds.TransformToAABB(visual.TransformToVisual((Visual)visual.GetVisualRoot()).Value);

        return bounds.Intersects(controlBounds);
    }

    /// <summary>
    /// 获取指定范围内的所有控件。
    /// </summary>
    /// <param name="root">要检查的根容器。</param>
    /// <param name="bounds">指定的范围（相对于根容器的坐标）。</param>
    /// <returns>在范围内的控件列表。</returns>
    public static List<Control> GetControlsInBounds(Control root, Rect bounds)
    {
        var visuals = root.GetVisualDescendants();
        var controlsInBounds = new List<Control>();

        foreach (var visual in visuals)
        {
            if (visual is Control control)
            {
                // 获取控件的边界框（相对于根容器的坐标）。
                var controlBounds = control.Bounds.TransformToAABB(control.TransformToVisual((Visual)visual.GetVisualRoot()).Value);

                if (bounds.Intersects(controlBounds))
                {
                    controlsInBounds.Add(control);
                }
            }
        }

        return controlsInBounds;
    }
}
