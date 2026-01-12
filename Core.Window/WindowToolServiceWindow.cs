using Avalonia;
using Avalonia.Threading;
using Core.SDKs.Services;
using Core.Services;
using Core.Services.Interfaces;
using Vanara.PInvoke;
using PluginCore;
using System.Collections.Generic;

namespace Core.Window;

public class WindowToolServiceWindow : IWindowTool
{
    private class OverlaySet
    {
        public TopMostBorderWindow Border { get; set; } = null!;
        public TopMostActionWindow Action { get; set; } = null!;
    }

    private readonly Dictionary<IntPtr, OverlaySet> _overlays = new();

    public void SetForegroundWindow(IntPtr hWnd)
    {
        User32.SetActiveWindow(hWnd);
        User32.SetForegroundWindow(hWnd);
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
}
