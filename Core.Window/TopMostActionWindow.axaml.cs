using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Vanara.PInvoke;

namespace Core.Window;

public partial class TopMostActionWindow : Avalonia.Controls.Window
{
    private readonly IntPtr _targetHwnd;
    private readonly DispatcherTimer _timer;

    public event Action? CancelTopMostRequested;

    public TopMostActionWindow()
    {
        InitializeComponent();
    }

    public TopMostActionWindow(IntPtr targetHwnd) : this()
    {
        _targetHwnd = targetHwnd;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _timer.Tick += UpdatePosition;
        _timer.Start();

        var btn = this.FindControl<Button>("CancelButton");
        if (btn != null)
        {
            btn.Click += CancelButton_Click;
        }

        this.Opened += (s, e) =>
        {
            var handle = this.TryGetPlatformHandle()?.Handle;
            if (handle.HasValue)
            {
                // ToolWindow style
                var exStyle = User32.GetWindowLong(handle.Value, User32.WindowLongFlags.GWL_EXSTYLE);
                User32.SetWindowLong(handle.Value, User32.WindowLongFlags.GWL_EXSTYLE, exStyle | (int)User32.WindowStylesEx.WS_EX_TOOLWINDOW);
            }
        };
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        CancelTopMostRequested?.Invoke();
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

        // Calculate position: Top Center of the target window
        var targetWidth = rect.Width / scaling;
        var targetX = rect.left;
        var targetY = rect.top;

        // Button/Window size
        var myWidth = Bounds.Width;
        var myHeight = Bounds.Height;
        if (myWidth == 0 || myHeight == 0) return; // Not measured yet

        var newX = targetX + (targetWidth - myWidth) / 2;
        var newY = targetY + 10; // 10px padding from top

        if (Math.Abs(Position.X - newX) > 1 || Math.Abs(Position.Y - newY) > 1)
        {
            Position = new PixelPoint((int)newX, (int)newY);
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
