// Author: liaom
// SolutionName: Kitopia
// ProjectName: Core.Window
// FileName:ToastShowWindow.axaml.cs
// Date: 2025/10/30 09:10
// FileEffect:

using Avalonia;
using Vanara.PInvoke;

namespace KitopiaAvalonia.Services;

public partial class ToastShowWindow : Avalonia.Controls.Window
{
    public ToastShowWindow()
    {
        InitializeComponent();
        //修改窗口位置

        MoveToRightBottom();
    }

    private void MoveToRightBottom()
    {
        User32.GetCursorPos(out var pos);
        var hmonitor = User32.MonitorFromPoint(pos, User32.MonitorFlags.MONITOR_DEFAULTTOPRIMARY);
        var monitorInfo = new User32.MONITORINFO();
        monitorInfo.cbSize = 40;
        User32.GetMonitorInfo(hmonitor, ref monitorInfo);
        User32.GetWindowRect(this.TryGetPlatformHandle()!.Handle, out var windowRect);
        this.Position = new PixelPoint(
            monitorInfo.rcWork.Right - windowRect.Width - 10,
            monitorInfo.rcWork.Bottom - windowRect.Height - 10);
    }

    protected override void IsVisibleChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.IsVisibleChanged(e);
        if (e.NewValue is true)
        {
            MoveToRightBottom();
        }
    }
}