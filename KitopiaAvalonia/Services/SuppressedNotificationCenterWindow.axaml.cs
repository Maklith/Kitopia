using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Ursa.Controls;
using Vanara.PInvoke;

namespace KitopiaAvalonia.Services;

public partial class SuppressedNotificationCenterWindow : UrsaWindow
{
    private bool _allowClose;

    public SuppressedNotificationCenterWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    public void ClosePermanently()
    {
        _allowClose = true;
        Close();
    }

    public void RepositionNearCursor()
    {
        User32.GetCursorPos(out var pos);
        var screen = Screens.ScreenFromPoint(new PixelPoint(pos.X, pos.Y)) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var margin = 10;
        var workingArea = screen.WorkingArea;
        var width = Math.Max(1, (int)Math.Ceiling((Bounds.Width > 0 ? Bounds.Width : Width) * RenderScaling));
        var height = Math.Max(1, (int)Math.Ceiling((Bounds.Height > 0 ? Bounds.Height : Height) * RenderScaling));

        var targetX = workingArea.Right - width - margin;
        var targetY = workingArea.Bottom - height - margin;

        var minX = workingArea.X + margin;
        var minY = workingArea.Y + margin;
        var maxX = Math.Max(minX, workingArea.Right - width - margin);
        var maxY = Math.Max(minY, workingArea.Bottom - height - margin);

        targetX = Math.Clamp(targetX, minX, maxX);
        targetY = Math.Clamp(targetY, minY, maxY);

        Position = new PixelPoint(targetX, targetY);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void WindowBase_OnDeactivated(object? sender, EventArgs e) {
        Hide();
    }
}
