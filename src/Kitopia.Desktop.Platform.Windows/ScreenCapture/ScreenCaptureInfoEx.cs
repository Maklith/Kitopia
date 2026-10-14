// Author: liaom
// SolutionName: Kitopia
// ProjectName: Kitopia.Desktop.Platform.Windows
// FileName:ScreenCaptureInfoEx.cs
// Date: 2026/04/17 12:04
// FileEffect:

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using PluginCore;
using Vanara.PInvoke;

namespace Kitopia.Desktop.Platform.Windows.ScreenCapture;

public static class ScreenCaptureInfoEx {
    private const int DwmwaExtendedFrameBounds = 9;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    static RECT IntersectRects(RECT rect1, RECT rect2) {
        RECT result = new RECT {
            Left = Math.Max(rect1.Left, rect2.Left),
            Top = Math.Max(rect1.Top, rect2.Top),
            Right = Math.Min(rect1.Right, rect2.Right),
            Bottom = Math.Min(rect1.Bottom, rect2.Bottom)
        };

        // 如果没有交集
        if (result.Right < result.Left || result.Bottom < result.Top) {
            result = new RECT { Left = 0, Top = 0, Right = 0, Bottom = 0 };
        }

        return result;
    }

    public static Rect GetVisibleWindowRectForSelection(Rect windowRect, Rect? extendedFrameRect, Rect screenRect) {
        var targetRect = extendedFrameRect ?? windowRect;
        var visibleRect = IntersectRects(ToNativeRect(targetRect), ToNativeRect(screenRect));
        return new Rect(visibleRect.X, visibleRect.Y, visibleRect.Width, visibleRect.Height);
    }

    private static RECT ToNativeRect(Rect rect) {
        return new RECT {
            Left = rect.X,
            Top = rect.Y,
            Right = rect.X + rect.Width,
            Bottom = rect.Y + rect.Height
        };
    }

    private static bool TryGetExtendedFrameBounds(HWND hwnd, out RECT rect) {
        return DwmGetWindowAttribute(hwnd.DangerousGetHandle(), DwmwaExtendedFrameBounds, out rect, Marshal.SizeOf<RECT>()) == 0;
    }

    private const int WsExToolwindow = 0x00000080; // 工具窗口

    public static IEnumerable<WindowInfo> GetAllWindowInfo() {
        HWND currentHwnd = User32.GetTopWindow(IntPtr.Zero);
        int zIndex = 0;
        while (currentHwnd != IntPtr.Zero) {
            zIndex++;
            currentHwnd = User32.GetWindow(currentHwnd, User32.GetWindowCmd.GW_HWNDNEXT);
            // 忽略有父窗口的和不可见的窗口
            if (!User32.IsWindowVisible(currentHwnd)) {
                continue;
            }

            int style2 = User32.GetWindowLong(currentHwnd, User32.WindowLongFlags.GWL_EXSTYLE);
            if ((style2 & (int)User32.WindowStylesEx.WS_EX_NOACTIVATE) != 0) {
                continue;
            }

            if ((style2 & WsExToolwindow) != 0) {
                continue;
            }

            if (!User32.IsWindow(currentHwnd)) {
                continue;
            }

            User32.GetWindowDisplayAffinity(currentHwnd, out var affinity);
            if (affinity != User32.WindowDisplayAffinity.WDA_NONE) {
                continue;
            }

            User32.GetWindowThreadProcessId(currentHwnd, out var id);

            var s = Process.GetProcessById((int)id).ProcessName;


            // 获取窗口标题
            StringBuilder stringBuilder = new StringBuilder(100);
            User32.GetWindowText(currentHwnd, stringBuilder, 100);
            var title = stringBuilder.ToString();
            if (string.IsNullOrWhiteSpace(title)) {
                continue;
            }

            // 获取窗口的位置和大小
            User32.GetWindowRect(currentHwnd, out var windowRect);
            var selectionRect = TryGetExtendedFrameBounds(currentHwnd, out var extendedFrameRect)
                ? extendedFrameRect
                : windowRect;

            //User32.GetClientRect(currentHwnd, out RECT clientRect);
            var hMonitor = User32.MonitorFromWindow(currentHwnd, User32.MonitorFlags.MONITOR_DEFAULTTONEAREST);

            // 获取监视器信息（包括屏幕工作区域）
            User32.MONITORINFO monitorInfo = new User32.MONITORINFO
                { cbSize = (uint)Marshal.SizeOf(typeof(User32.MONITORINFO)) };
            User32.GetMonitorInfo(hMonitor, ref monitorInfo);

            RECT screenRect = monitorInfo.rcWork;

            // 计算窗口与屏幕的可见区域交集
            RECT visibleRect = IntersectRects(selectionRect, screenRect);

            if (visibleRect.Width > 0 && visibleRect.Height > 0) {
                yield return new WindowInfo {
                    Title = title,
                    ModuleFileName = s,
                    Hwnd = currentHwnd.DangerousGetHandle(),
                    Rect = new Rect(visibleRect.X, visibleRect.Y, visibleRect.Width, visibleRect.Height),
                    ZIndex = zIndex
                };
            }
        }
    }

    extension(ref ScreenCaptureInfo screenCaptureInfo) {
        //确保指针存在且有效
        private bool ValidScreenIntptr() {
            if (screenCaptureInfo.HMonitor == IntPtr.Zero || !screenCaptureInfo.ScreenInfo.HasValue) {
                return false;
            }

            User32.MONITORINFO info = new User32.MONITORINFO();
            User32.GetMonitorInfo(screenCaptureInfo.HMonitor, ref info);
            var (i, y, width, height) = screenCaptureInfo.ScreenInfo.Value;
            return info.rcMonitor.left == i && info.rcMonitor.top == y &&
                   info.rcMonitor.right - info.rcMonitor.left == width &&
                   info.rcMonitor.bottom - info.rcMonitor.top == height;
        }


        public void ThrowIfCantGetValidScreenIntptr() {
            if (screenCaptureInfo.ValidScreenIntptr()) {
                return;
            }

            IntPtr h = IntPtr.Zero;
            var screenInfo = screenCaptureInfo.ScreenInfo;
            if (!screenInfo.HasValue) {
                throw new Exception("目标显示器不存在");
            }

            User32.EnumDisplayMonitors(default, null, (arg1, _, arg3, _) => {
                if (arg3 != null &&
                    screenInfo.Value.X == arg3.left && screenInfo.Value.Y == arg3.top &&
                    screenInfo.Value.Width == arg3.right - arg3.left &&
                    screenInfo.Value.Height == arg3.bottom - arg3.top) {
                    h = arg1.DangerousGetHandle();
                    return false;
                }

                return true;
            }, IntPtr.Zero);
            if (h == IntPtr.Zero) {
                throw new Exception("目标显示器不存在");
            }

            screenCaptureInfo.HMonitor = h;
            screenCaptureInfo.SdrWhiteLevelScale=DisplayConfigHelper.GetSdrWhiteLevel(h);
        }
        public void ThrowIfCantGetValidWindowHandle() {
            screenCaptureInfo.ThrowIfCantGetValidScreenIntptr();
            _ = screenCaptureInfo.WindowInfo??throw new Exception("目标窗口不存在");
            

            var windowInfo = screenCaptureInfo.WindowInfo.Value;
            var allWindowInfo = GetAllWindowInfo();
            var windowInfos = allWindowInfo.ToList();
            if (!windowInfos.Any(e =>
                    e.Hwnd == windowInfo.Hwnd && e.Title == windowInfo.Title &&
                    e.ModuleFileName == windowInfo.ModuleFileName)) {
                if (windowInfos.Any(e =>
                        e.Title == windowInfo.Title && e.ModuleFileName == windowInfo.ModuleFileName)) {
                    screenCaptureInfo.WindowInfo = windowInfos.First(e =>
                        e.Title == windowInfo.Title && e.ModuleFileName == windowInfo.ModuleFileName);
                }
                else if (windowInfos.Any(e => e.ModuleFileName == windowInfo.ModuleFileName)) {
                    screenCaptureInfo.WindowInfo =
                        windowInfos.First(e => e.ModuleFileName == windowInfo.ModuleFileName);
                }
                else {
                    throw new Exception("目标窗口不存在");
                }
            }
            var monitorFromWindow =
                User32.MonitorFromWindow(windowInfo.Hwnd, User32.MonitorFlags.MONITOR_DEFAULTTONEAREST);
            screenCaptureInfo.HMonitor = monitorFromWindow.DangerousGetHandle();
            screenCaptureInfo.SdrWhiteLevelScale = DisplayConfigHelper.GetSdrWhiteLevel(screenCaptureInfo.HMonitor);
        }
    }
}
