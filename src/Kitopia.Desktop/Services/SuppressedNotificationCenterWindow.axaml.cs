using System;
using Avalonia;
using Avalonia.Controls;
using Ursa.Controls;
using Vanara.PInvoke;

namespace Kitopia.Desktop.Services;

public partial class SuppressedNotificationCenterWindow : UrsaWindow
{
    private readonly Size _preferredSize;
    private PixelPoint _anchorPoint;
    private bool _allowClose;

    public SuppressedNotificationCenterWindow()
    {
        InitializeComponent();
        _preferredSize = new Size(Width, Height);
        Closing += OnClosing;
        Opened += (_, _) => Reposition();
    }

    public void ClosePermanently()
    {
        _allowClose = true;
        Close();
    }

    public void RepositionNearCursor()
    {
        User32.GetCursorPos(out var pos);
        _anchorPoint = new PixelPoint(pos.X, pos.Y);
        Reposition();
    }

    private void Reposition()
    {
        var screen = Screens.ScreenFromPoint(_anchorPoint) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var placement = GetPopupBounds(_anchorPoint, screen.WorkingArea, screen.Scaling, _preferredSize);
        Width = placement.Width / screen.Scaling;
        Height = placement.Height / screen.Scaling;
        Position = placement.Position;
    }

    internal static PixelRect GetPopupBounds(PixelPoint anchor, PixelRect workingArea, double scaling, Size preferredSize)
    {
        var margin = (int)Math.Ceiling(10 * scaling);
        var width = Math.Max(1, Math.Min((int)Math.Ceiling(preferredSize.Width * scaling), workingArea.Width - 2 * margin));
        var height = Math.Max(1, Math.Min((int)Math.Ceiling(preferredSize.Height * scaling), workingArea.Height - 2 * margin));

        var minX = workingArea.X + margin;
        var minY = workingArea.Y + margin;
        var maxX = Math.Max(minX, workingArea.Right - width - margin);
        var maxY = Math.Max(minY, workingArea.Bottom - height - margin);

        var targetX = Math.Clamp(anchor.X - width / 2, minX, maxX);
        var targetY = anchor.Y - height - margin;
        if (targetY < minY)
        {
            targetY = anchor.Y + margin;
        }

        return new PixelRect(targetX, Math.Clamp(targetY, minY, maxY), width, height);
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
