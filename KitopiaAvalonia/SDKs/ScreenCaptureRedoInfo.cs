using System.Collections.Generic;
using Avalonia;

namespace KitopiaAvalonia.SDKs;

public struct ScreenCaptureRedoInfo
{
    public 截图工具 Type;
    public object? Target;
    public ScreenCaptureEditType EditType;
    public Point startPoint;
    public Size Size;
    public IList<Point> points;
    public Point Point1;
    public Point Point2;
    public object Data;
}