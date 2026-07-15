using System.Collections.Generic;
using Avalonia;

namespace Kitopia.Desktop.SDKs;

public struct ScreenCaptureRedoInfo
{
    public 截图工具 Type;
    public object? Target;
    public ScreenCaptureEditType EditType;
    public Point StartPoint;
    public Size Size;
    public IList<Point>? Points;
    public Point Point1;
    public Point Point2;
    public object Data;
}