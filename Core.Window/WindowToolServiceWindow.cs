using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Core.Utils;
using PluginCore;
using Vanara.PInvoke;

namespace Core.Window;

public class WindowToolServiceWindow : IWindowTool
{
    private class OverlaySet
    {
        public TopMostBorderWindow Border { get; set; } = null!;
        public TopMostActionWindow Action { get; set; } = null!;
    }

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtr")]
    private static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetClassLong")]
    private static extern IntPtr GetClassLongPtr32(IntPtr hWnd, int nIndex);

    private static IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex)
    {
        if (IntPtr.Size > 4)
            return GetClassLongPtr64(hWnd, nIndex);
        else
            return GetClassLongPtr32(hWnd, nIndex);
    }

    private readonly Dictionary<IntPtr, OverlaySet> _overlays = new();

    public void SetForegroundWindow(IntPtr hWnd)
    {
        if (User32.IsIconic(hWnd))
        {
            User32.ShowWindow(hWnd, ShowWindowCommand.SW_RESTORE);
        }

        var foregroundWnd = User32.GetForegroundWindow();
        if (foregroundWnd == hWnd)
        {
            return;
        }

        var currentThreadId = Kernel32.GetCurrentThreadId();
        var windowThreadId = User32.GetWindowThreadProcessId(hWnd, out _);

        if (currentThreadId != windowThreadId)
        {
            User32.AttachThreadInput(currentThreadId, windowThreadId, true);
            User32.BringWindowToTop(hWnd);
            User32.SetForegroundWindow(hWnd);
            User32.AttachThreadInput(currentThreadId, windowThreadId, false);
        }
        else
        {
            User32.BringWindowToTop(hWnd);
            User32.SetForegroundWindow(hWnd);
        }
    }

    public void MoveWindowToMouseScreenCenter(Avalonia.Controls.Window window)
    {
        User32.GetCursorPos(out var pos);
        var hmonitor = User32.MonitorFromPoint(pos, User32.MonitorFlags.MONITOR_DEFAULTTOPRIMARY);
        var monitorInfo = new User32.MONITORINFO();
        monitorInfo.cbSize = 40;
        User32.GetMonitorInfo(hmonitor, ref monitorInfo);
        User32.GetWindowRect(window.TryGetPlatformHandle().Handle, out var windowRect);
        window.Position =
            new PixelPoint(
                monitorInfo.rcMonitor.Left + (int)((monitorInfo.rcMonitor.Width - windowRect.Width) / 2),
                monitorInfo.rcMonitor.Top + monitorInfo.rcMonitor.Height / 4);
    }

    public void SetWindowTopMost(IntPtr hWnd, bool topMost)
    {
        var hWndInsertAfter = topMost ? (HWND)((IntPtr)(-1)) : (HWND)((IntPtr)(-2));
        User32.SetWindowPos((HWND)hWnd, hWndInsertAfter, 0, 0, 0, 0, User32.SetWindowPosFlags.SWP_NOMOVE | User32.SetWindowPosFlags.SWP_NOSIZE);

        Dispatcher.UIThread.Invoke(() =>
        {
            if (topMost)
            {
                if (!_overlays.ContainsKey(hWnd))
                {
                    var set = new OverlaySet();
                    set.Border = new TopMostBorderWindow(hWnd);
                    set.Action = new TopMostActionWindow(hWnd);

                    set.Action.CancelTopMostRequested += () =>
                    {
                        SetWindowTopMost(hWnd, false);
                    };

                    // Handle manual closing if window disappears
                    set.Border.Closed += (s, e) => 
                    {
                        if (_overlays.ContainsKey(hWnd)) 
                        {
                            _overlays[hWnd].Action.Close();
                            _overlays.Remove(hWnd);
                        }
                    };

                     set.Action.Closed += (s, e) => 
                    {
                        if (_overlays.ContainsKey(hWnd)) 
                        {
                            _overlays[hWnd].Border.Close();
                            _overlays.Remove(hWnd);
                        }
                    };

                    set.Border.Show();
                    set.Action.Show();
                    _overlays[hWnd] = set;
                }
            }
            else
            {
                if (_overlays.TryGetValue(hWnd, out var set))
                {
                    set.Action.Close();
                    set.Border.Close();
                    _overlays.Remove(hWnd);
                }
            }
        });
    }

    public void SelectAndSetWindowTopMost()
    {
        var capture = (IScreenCaptureWindow?)ServiceManager.Services.GetService(typeof(IScreenCaptureWindow));
        capture?.RequestUserSelectScreenInfo(info =>
        {
            if (info.WindowInfo.Hwnd != IntPtr.Zero)
            {
                SetWindowTopMost(info.WindowInfo.Hwnd, true);
            }
        });
    }

    public IEnumerable<WindowInfo> GetAllWindows()
    {
        var list = new List<WindowInfo>();
        User32.EnumWindows((hwnd, lparam) =>
        {
            if (!User32.IsWindowVisible(hwnd)) return true;

            int length = User32.GetWindowTextLength(hwnd);
            if (length == 0) return true;

            var sb = new StringBuilder(length + 1);
            User32.GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();
            if (string.IsNullOrWhiteSpace(title)) return true;
            
            // Exclude ToolWindows
            var exStyle = (User32.WindowStylesEx)User32.GetWindowLong(hwnd, User32.WindowLongFlags.GWL_EXSTYLE);
            if ((exStyle & User32.WindowStylesEx.WS_EX_TOOLWINDOW) != 0) return true;

            if (hwnd == User32.GetShellWindow()) return true;

            User32.GetWindowThreadProcessId(hwnd, out var processId);
            string moduleName = null;
            try
            {
                using var process = Process.GetProcessById((int)processId);
                moduleName = process.ProcessName;
            }
            catch
            {
                // Ignore windows from processes we can't access
            }
            
            User32.GetWindowRect(hwnd, out var rect);

            list.Add(new WindowInfo
            {
                Hwnd = (IntPtr)hwnd,
                Title = title,
                ModuleFileName = moduleName,
                Rect = new PluginCore.Rect(rect.left, rect.top, rect.Width, rect.Height)
            });

            return true;
        }, IntPtr.Zero);
        return list;
    }
    public Bitmap? GetWindowIcon(IntPtr hWnd)
    {
        try
        {
            var hIcon = User32.SendMessage(hWnd, User32.WindowMessage.WM_GETICON, (IntPtr)1 /*ICON_BIG*/, IntPtr.Zero);
            if (hIcon == IntPtr.Zero)
            {
                hIcon = User32.SendMessage(hWnd, User32.WindowMessage.WM_GETICON, (IntPtr)0 /*ICON_SMALL*/, IntPtr.Zero);
            }
            
            if (hIcon == IntPtr.Zero)
            {
                hIcon = GetClassLongPtr(hWnd, -14 /*GCL_HICON*/);
            }
            
            if (hIcon == IntPtr.Zero)
            {
                hIcon = GetClassLongPtr(hWnd, -34 /*GCL_HICONSM*/);
            }

            if (hIcon != IntPtr.Zero)
            {
                using var icon = System.Drawing.Icon.FromHandle(hIcon);
                using var bitmap = icon.ToBitmap();
                var avaloniaBitmap = ((System.Drawing.Bitmap)bitmap).ToAvaloniaBitmap();
                return avaloniaBitmap;
            }
            
            User32.GetWindowThreadProcessId(hWnd, out var processId);
            if (processId != 0)
            {
                try
                {
                    using var process = Process.GetProcessById((int)processId);
                    var fileName = process.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(fileName) && File.Exists(fileName))
                    {
                        using var icon = IconTools.GetIconFromImageList(fileName);
                        if (icon != null)
                        {
                            using var bitmap = icon.ToBitmap();
                            return ((System.Drawing.Bitmap)bitmap).ToAvaloniaBitmap();
                        }
                    }
                }
                catch
                {
                    // Ignore process access errors
                }
            }
        }
        catch
        {
            // Ignore errors
        }
        return null;
    }
}
