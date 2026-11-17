using System.Runtime.InteropServices;
using System.Text;
using Vanara.PInvoke;

namespace Kitopia.Desktop.Platform.Windows;

/// <summary>
/// 检测 Windows 前景或指定窗口是否处于全屏应用状态。
/// </summary>
public static class WindowFullScreenDetector
{
    private const int FullScreenRectTolerance = 2;

    public static bool IsForegroundFullScreen() => IsFullScreenWindow(User32.GetForegroundWindow());

    public static bool IsFullScreenWindow(HWND window)
    {
        if (!OperatingSystem.IsWindows() || window.IsNull)
        {
            return false;
        }

        if (window == User32.GetShellWindow() || window == User32.GetDesktopWindow())
        {
            return false;
        }

        if (User32.IsIconic(window) || !User32.IsWindowVisible(window))
        {
            return false;
        }

        User32.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0 || processId == Environment.ProcessId)
        {
            return false;
        }

        var className = new StringBuilder(64);
        if (User32.GetClassName(window, className, className.Capacity) > 0 &&
            IsDesktopShellClassName(className.ToString()))
        {
            return false;
        }

        if (!User32.GetWindowRect(window, out var windowRect) ||
            windowRect.Width <= 0 || windowRect.Height <= 0)
        {
            return false;
        }

        var monitor = User32.MonitorFromWindow(window, User32.MonitorFlags.MONITOR_DEFAULTTONULL);
        var monitorInfo = new User32.MONITORINFO
        {
            cbSize = (uint)Marshal.SizeOf<User32.MONITORINFO>()
        };
        if (monitor.IsNull || !User32.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        return IsWindowRectFullScreen(windowRect, monitorInfo.rcMonitor);
    }

    internal static bool IsDesktopShellClassName(string className) =>
        string.Equals(className, "Progman", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(className, "WorkerW", StringComparison.OrdinalIgnoreCase);

    internal static bool IsWindowRectFullScreen(RECT windowRect, RECT monitorRect)
    {
        if (windowRect.Width <= 0 || windowRect.Height <= 0)
        {
            return false;
        }

        return Math.Abs(windowRect.left - monitorRect.left) <= FullScreenRectTolerance
               && Math.Abs(windowRect.top - monitorRect.top) <= FullScreenRectTolerance
               && Math.Abs(windowRect.right - monitorRect.right) <= FullScreenRectTolerance
               && Math.Abs(windowRect.bottom - monitorRect.bottom) <= FullScreenRectTolerance;
    }
}
