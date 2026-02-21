using Avalonia;
using Avalonia.Threading;
using Vanara.PInvoke;

namespace Core.Window;

public partial class TopMostBorderWindow : Avalonia.Controls.Window
{
    private readonly IntPtr _targetHwnd;
    private readonly DispatcherTimer _timer;

    public TopMostBorderWindow()
    {
        InitializeComponent();
    }

    public TopMostBorderWindow(IntPtr targetHwnd) : this()
    {
        _targetHwnd = targetHwnd;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _timer.Tick += UpdatePosition;
        _timer.Start();

        // Ensure click-through
        this.Opened += (s, e) =>
        {
            var handle = this.TryGetPlatformHandle()?.Handle;
            if (handle.HasValue)
            {
                var exStyle = User32.GetWindowLong(handle.Value, User32.WindowLongFlags.GWL_EXSTYLE);
                var newStyle = exStyle | 
                               (int)User32.WindowStylesEx.WS_EX_TRANSPARENT | 
                               (int)User32.WindowStylesEx.WS_EX_TOOLWINDOW | 
                               (int)User32.WindowStylesEx.WS_EX_LAYERED;
                User32.SetWindowLong(handle.Value, User32.WindowLongFlags.GWL_EXSTYLE, newStyle);
                
                 User32.SetWindowPos((HWND)handle.Value, (HWND)IntPtr.Zero, 0, 0, 0, 0, 
                     User32.SetWindowPosFlags.SWP_NOMOVE | 
                     User32.SetWindowPosFlags.SWP_NOSIZE | 
                     User32.SetWindowPosFlags.SWP_NOZORDER | 
                     User32.SetWindowPosFlags.SWP_FRAMECHANGED | 
                     User32.SetWindowPosFlags.SWP_NOACTIVATE);
            }
        };
    }

    private void UpdatePosition(object? sender, EventArgs e)
    {
        if (!User32.IsWindow(_targetHwnd))
        {
            Close();
            return;
        }

        if (User32.IsIconic((HWND)_targetHwnd) || !User32.IsWindowVisible((HWND)_targetHwnd))
        {
            IsVisible = false;
            return;
        }
        else
        {
            if (!IsVisible) IsVisible = true;
        }

        User32.GetWindowRect((HWND)_targetHwnd, out var rect);

        var screen = Screens.ScreenFromPoint(new PixelPoint(rect.left, rect.top));
        double scaling = screen?.Scaling ?? 1.0;

        var width = rect.Width / scaling;
        var height = rect.Height / scaling;

        if (Math.Abs(Width - width) > 1 || Math.Abs(Height - height) > 1 ||
            Math.Abs(Position.X - rect.left) > 1 || Math.Abs(Position.Y - rect.top) > 1)
        {
            Position = new PixelPoint(rect.left, rect.top);
            Width = width;
            Height = height;
        }
        // Force TopMost
        var handle = this.TryGetPlatformHandle()?.Handle;
        if (handle.HasValue)
        {
           User32.SetWindowPos((HWND)handle.Value, (HWND)(-1), 0, 0, 0, 0, User32.SetWindowPosFlags.SWP_NOMOVE | User32.SetWindowPosFlags.SWP_NOSIZE | User32.SetWindowPosFlags.SWP_NOACTIVATE);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer?.Stop();
        base.OnClosed(e);
    }
}
