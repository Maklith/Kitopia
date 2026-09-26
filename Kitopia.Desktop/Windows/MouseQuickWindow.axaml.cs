using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Kitopia.Desktop.Features.Search.ViewModels;
using Kitopia.Desktop.Features.Services;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Kitopia.Desktop.Windows;

public partial class MouseQuickWindow : Window
{
    private MouseQuickWindowViewModel? _viewModel;
    public PixelRect? AnchorBounds { get; set; }
    public nint OriginalWindowHandle { get; set; }

    public MouseQuickWindow()
    {
        InitializeComponent();
        PreviewSurface.CloseRequested += Close;
        Focusable = true;
        Deactivated += (_, _) => Close();
        Activated += (_, _) => Focus();
        DataContextChanged += (_, _) =>
        {
            _viewModel = DataContext as MouseQuickWindowViewModel;
        };
    }

    public void ActivateAndFocus()
    {
        Activate();
        Focus();
        BringToForeground();
        Dispatcher.UIThread.Post(() =>
        {
            Activate();
            Focus();
            BringToForeground();
        }, DispatcherPriority.Input);
    }

    private void BringToForeground()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero)
        {
            ServiceManager.Services?.GetService<IWindowTool>()?.SetForegroundWindow(handle);
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        var screen = AnchorBounds is { } anchor ? Screens.ScreenFromPoint(anchor.Position) : Screens.ScreenFromWindow(this);
        if (screen is null) return;
        var area = screen.WorkingArea;
        var placement = GetPreviewBounds(AnchorBounds, area, screen.Scaling, new Size(Width, Height));
        MinWidth = Math.Min(MinWidth, placement.Width / screen.Scaling);
        MinHeight = Math.Min(MinHeight, placement.Height / screen.Scaling);
        Width = placement.Width / screen.Scaling;
        Height = placement.Height / screen.Scaling;
        Position = placement.Position;
        ActivateAndFocus();
    }

    internal static PixelRect GetPreviewBounds(PixelRect? anchor, PixelRect area, double scaling, Size desiredSize)
    {
        var width = Math.Min((int)(desiredSize.Width * scaling), area.Width);
        var height = Math.Min((int)(desiredSize.Height * scaling), area.Height);
        if (anchor is not { } item)
            return new PixelRect(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
        var gap = (int)Math.Ceiling(8 * scaling);
        var below = Math.Max(0, area.Bottom - item.Bottom - gap);
        var above = Math.Max(0, item.Y - area.Y - gap);
        var useBelow = below >= height || below >= above;
        var availableHeight = useBelow ? below : above;
        if (availableHeight > 0) height = Math.Min(height, availableHeight);
        var x = Math.Clamp(item.X + (item.Width - width) / 2, area.X, area.Right - width);
        var y = Math.Clamp(useBelow ? item.Bottom + gap : item.Y - gap - height, area.Y, area.Bottom - height);
        return new PixelRect(x, y, width, height);
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e) => Close();

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    private void FullScreenButton_OnClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Left when _viewModel?.PreviousCommand.CanExecute(null) == true:
                _viewModel.PreviousCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right when _viewModel?.NextCommand.CanExecute(null) == true:
                _viewModel.NextCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter when _viewModel?.OpenFileCommand.CanExecute(null) == true:
                _viewModel.OpenFileCommand.Execute(null);
                Close();
                e.Handled = true;
                break;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        PreviewSurface.DataContext = null;
        if (_viewModel is not null)
        {
            _viewModel.Dispose();
        }
        RestoreOriginalWindowFocus();
        base.OnClosed(e);
    }

    private void RestoreOriginalWindowFocus()
    {
        var handle = OriginalWindowHandle;
        OriginalWindowHandle = IntPtr.Zero;
        if (handle == IntPtr.Zero) return;

        Dispatcher.UIThread.Post(() =>
            ServiceManager.Services?.GetService<IWindowTool>()?.SetForegroundWindow(handle),
            DispatcherPriority.Input);
    }
}
