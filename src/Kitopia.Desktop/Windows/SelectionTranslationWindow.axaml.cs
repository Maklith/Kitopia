using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Kitopia.Desktop.Abstractions.TextSelection;
using Kitopia.Desktop.ViewModels;
using PluginCore;

namespace Kitopia.Desktop.Windows;

public partial class SelectionTranslationWindow : Window
{
    private const double DefaultWidth = 440;
    private const double DefaultMinWidth = 360;
    private const double DefaultMaxWidth = 620;
    private const double DefaultMaxHeight = 620;
    private const double DefaultFallbackHeight = 240;
    private const int SelectionWindowBoundsTolerance = 16;

    private readonly IWindowTool _windowTool;
    private PixelPoint? _anchor;
    private PixelPoint? _screenAnchor;

    public SelectionTranslationWindow(SelectionTranslationWindowViewModel viewModel, IWindowTool windowTool)
    {
        InitializeComponent();
        _windowTool = windowTool;
        ViewModel = viewModel;
        DataContext = viewModel;
        SizeChanged += (_, _) =>
        {
            if (IsVisible && (_anchor is not null || _screenAnchor is not null) && !ViewModel.IsPinned)
                PositionWindow();
        };
        ScalingChanged += (_, _) =>
        {
            if (IsVisible && (_anchor is not null || _screenAnchor is not null) && !ViewModel.IsPinned)
                PositionWindow();
        };
    }

    public SelectionTranslationWindowViewModel ViewModel { get; }

    public void ShowAt(PixelPoint? anchor, PixelPoint? screenAnchor = null)
    {
        if (anchor is not null)
        {
            _anchor = anchor;
            _screenAnchor = screenAnchor ?? anchor;
        }
        else if (screenAnchor is not null)
        {
            _screenAnchor = screenAnchor;
        }

        if (!IsVisible)
        {
            PositionWindow();
            Show();
            Dispatcher.UIThread.Post(PositionWindow, DispatcherPriority.Loaded);
        }
        else
        {
            PositionWindow();
        }
        // Windows can clear WS_EX_TOPMOST while Avalonia still caches Topmost = true.
        if (TryGetPlatformHandle()?.Handle is { } handle)
            _windowTool.SetWindowTopMost(handle, true, showOverlay: false);
    }

    public void SetAnchor(PixelPoint? anchor)
    {
        _anchor = anchor;
        _screenAnchor = anchor;
        if (IsVisible) PositionWindow();
    }

    private void PositionWindow()
    {
        var anchor = _anchor ?? _screenAnchor ?? Position;
        var screen = FindScreen(_screenAnchor ?? anchor);
        if (screen is null) return;

        var scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
        var area = screen.WorkingArea;
        var availableDipWidth = Math.Max(1, area.Width / scale);
        var availableDipHeight = Math.Max(1, area.Height / scale);

        var maxWidth = Math.Min(DefaultMaxWidth, availableDipWidth);
        var minWidth = Math.Min(DefaultMinWidth, maxWidth);
        var maxHeight = Math.Min(DefaultMaxHeight, availableDipHeight);
        if (MaxWidth != maxWidth) MaxWidth = maxWidth;
        if (MinWidth != minWidth) MinWidth = minWidth;
        if (MaxHeight != maxHeight) MaxHeight = maxHeight;

        var dipWidth = Bounds.Width > 0
            ? Bounds.Width
            : (!double.IsNaN(Width) && Width > 0 ? Width : DefaultWidth);
        dipWidth = Math.Clamp(dipWidth, minWidth, maxWidth);

        var dipHeight = Bounds.Height > 0
            ? Bounds.Height
            : (DesiredSize.Height > 0
                ? DesiredSize.Height
                : (!double.IsNaN(Height) && Height > 0 ? Height : DefaultFallbackHeight));
        dipHeight = Math.Clamp(dipHeight, 1, maxHeight);

        var newPosition = CalculateWindowPosition(
            anchor,
            area,
            scale,
            new Size(dipWidth, dipHeight),
            RenderScaling);
        if (Position != newPosition)
            Position = newPosition;
    }

    private Screen? FindScreen(PixelPoint point)
    {
        var screens = Screens.All;
        if (screens.Count > 0)
        {
            var bounds = new PixelRect[screens.Count];
            for (var index = 0; index < screens.Count; index++)
                bounds[index] = screens[index].Bounds;

            var matched = FindScreenContainingOrNearest(bounds, point);
            if (matched >= 0) return screens[matched];
        }

        return Screens.ScreenFromPoint(point) ?? Screens.Primary;
    }

    internal static (int ScreenIndex, PixelPoint Anchor)? ResolvePlacementTarget(
        IReadOnlyList<PixelRect> screens,
        PixelPoint? releasePosition,
        TextSelectionBounds? selectionBounds,
        PixelRect? sourceWindowBounds,
        PixelPoint? pressPosition)
    {
        if (screens.Count == 0)
            return null;

        if (selectionBounds is { Width: > 0, Height: > 0 } sel)
        {
            var selRect = new PixelRect(sel.X, sel.Y, sel.Width, sel.Height);
            var selScreen = FindScreenContaining(screens, selRect.Center);
            var withinWindow = sourceWindowBounds is not { Width: > 0, Height: > 0 } win ||
                               RectanglesIntersect(selRect, Expand(win, SelectionWindowBoundsTolerance));
            if (selScreen >= 0 && withinWindow)
            {
                if (releasePosition is { } rp)
                {
                    var rpScreen = FindScreenContaining(screens, rp);
                    if (rpScreen == selScreen)
                        return (selScreen, rp);

                    if (rpScreen >= 0 &&
                        RectanglesIntersect(screens[rpScreen], selRect) &&
                        (sourceWindowBounds is not { Width: > 0, Height: > 0 } windowRect ||
                         Contains(windowRect, rp)))
                        return (rpScreen, rp);
                }

                var selectionAnchor = new PixelPoint(sel.Right, sel.Bottom);
                return (selScreen, ClampPointInside(selectionAnchor, screens[selScreen]));
            }
        }

        if (sourceWindowBounds is { Width: > 0, Height: > 0 } winBounds)
        {
            if (releasePosition is { } rp)
            {
                if (Contains(winBounds, rp))
                {
                    var rpScreen = FindScreenContaining(screens, rp);
                    if (rpScreen >= 0)
                        return (rpScreen, rp);
                }

                var clamped = ClampPointInside(rp, winBounds);
                var screenIndex = FindScreenContainingOrNearest(screens, clamped);
                if (pressPosition is { } pp && Contains(winBounds, pp))
                {
                    var ppScreen = FindScreenContaining(screens, pp);
                    if (ppScreen >= 0 &&
                        ppScreen != screenIndex &&
                        RectanglesIntersect(screens[ppScreen], winBounds) &&
                        DistanceSquaredToRect(clamped, screens[ppScreen]) <=
                        SelectionWindowBoundsTolerance * SelectionWindowBoundsTolerance)
                    {
                        return (ppScreen, ClampPointInside(clamped, screens[ppScreen]));
                    }
                }

                return (screenIndex, clamped);
            }

            var center = winBounds.Center;
            return (FindScreenContainingOrNearest(screens, center), center);
        }

        if (releasePosition is { } fallbackPoint)
            return (FindScreenContainingOrNearest(screens, fallbackPoint), fallbackPoint);

        return null;
    }

    internal static PixelPoint CalculateWindowPosition(
        PixelPoint anchor,
        PixelRect workingArea,
        double targetScaling,
        Size dipSize,
        double currentScaling = 0)
    {
        var scale = targetScaling > 0 ? targetScaling : 1.0;
        var activeScale = currentScaling > 0 ? currentScaling : scale;
        var effectiveScale = Math.Max(scale, activeScale);

        var width = Math.Clamp((int)Math.Ceiling(dipSize.Width * effectiveScale), 1, Math.Max(1, workingArea.Width));
        var height = Math.Clamp((int)Math.Ceiling(dipSize.Height * effectiveScale), 1, Math.Max(1, workingArea.Height));
        var gap = (int)Math.Ceiling(8 * scale);

        var clampedAnchorX = Math.Clamp(anchor.X, workingArea.X, workingArea.Right);
        var clampedAnchorY = Math.Clamp(anchor.Y, workingArea.Y, workingArea.Bottom);

        var x = clampedAnchorX + gap;
        var y = clampedAnchorY + gap;
        if (x + width > workingArea.Right) x = clampedAnchorX - gap - width;
        if (y + height > workingArea.Bottom) y = clampedAnchorY - gap - height;

        var maxX = Math.Max(workingArea.X, workingArea.Right - width);
        var maxY = Math.Max(workingArea.Y, workingArea.Bottom - height);
        return new PixelPoint(
            Math.Clamp(x, workingArea.X, maxX),
            Math.Clamp(y, workingArea.Y, maxY));
    }

    internal static PixelPoint ClampPointInside(PixelPoint point, PixelRect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return rect.Position;

        return new PixelPoint(
            Math.Clamp(point.X, rect.X, rect.Right - 1),
            Math.Clamp(point.Y, rect.Y, rect.Bottom - 1));
    }

    private static int FindScreenContaining(IReadOnlyList<PixelRect> screens, PixelPoint point)
    {
        for (var index = 0; index < screens.Count; index++)
        {
            if (Contains(screens[index], point))
                return index;
        }

        return -1;
    }

    private static int FindScreenContainingOrNearest(IReadOnlyList<PixelRect> screens, PixelPoint point)
    {
        var containing = FindScreenContaining(screens, point);
        if (containing >= 0) return containing;

        var bestIndex = -1;
        var bestDistance = long.MaxValue;
        for (var index = 0; index < screens.Count; index++)
        {
            var distance = DistanceSquaredToRect(point, screens[index]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    private static long DistanceSquaredToRect(PixelPoint point, PixelRect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return long.MaxValue;

        var dx = point.X < rect.X
            ? (long)rect.X - point.X
            : point.X >= rect.Right
                ? (long)point.X - (rect.Right - 1)
                : 0;
        var dy = point.Y < rect.Y
            ? (long)rect.Y - point.Y
            : point.Y >= rect.Bottom
                ? (long)point.Y - (rect.Bottom - 1)
                : 0;
        return dx * dx + dy * dy;
    }

    private static bool Contains(PixelRect rect, PixelPoint point) =>
        rect.Width > 0 && rect.Height > 0 &&
        point.X >= rect.X && point.X < rect.Right &&
        point.Y >= rect.Y && point.Y < rect.Bottom;

    private static bool RectanglesIntersect(PixelRect a, PixelRect b) =>
        a.Width > 0 && a.Height > 0 &&
        b.Width > 0 && b.Height > 0 &&
        a.X < b.Right && a.Right > b.X &&
        a.Y < b.Bottom && a.Bottom > b.Y;

    private static PixelRect Expand(PixelRect rect, int padding) =>
        new(rect.X - padding, rect.Y - padding, rect.Width + padding * 2, rect.Height + padding * 2);

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _anchor = null;
            _screenAnchor = null;
            BeginMoveDrag(e);
        }
    }

    private void PinButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.IsPinned = !ViewModel.IsPinned;
    }

    private void ExcludeCurrentProcess_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.ExcludeCurrentProcessCommand.CanExecute(null))
            ViewModel.ExcludeCurrentProcessCommand.Execute(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }
}
