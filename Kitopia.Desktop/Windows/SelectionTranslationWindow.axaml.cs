using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Kitopia.Desktop.ViewModels;

namespace Kitopia.Desktop.Windows;

public partial class SelectionTranslationWindow : Window
{
    private PixelPoint? _anchor;

    public SelectionTranslationWindow(SelectionTranslationWindowViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        SizeChanged += (_, _) =>
        {
            if (IsVisible && _anchor is not null && !ViewModel.IsPinned) PositionWindow();
        };
    }

    public SelectionTranslationWindowViewModel ViewModel { get; }

    public void ShowAt(PixelPoint? anchor)
    {
        _anchor = anchor ?? _anchor;
        if (!IsVisible)
        {
            Show();
            Dispatcher.UIThread.Post(PositionWindow, DispatcherPriority.Loaded);
        }
        else
        {
            PositionWindow();
        }
    }

    public void SetAnchor(PixelPoint? anchor)
    {
        _anchor = anchor;
        if (IsVisible) PositionWindow();
    }

    private void PositionWindow()
    {
        var point = _anchor ?? Position;
        var screen = Screens.ScreenFromPoint(point) ?? Screens.Primary;
        if (screen is null) return;

        var scaling = screen.Scaling;
        var area = screen.WorkingArea;
        var width = Math.Min((int)Math.Ceiling(Bounds.Width * scaling), area.Width);
        var height = Math.Min((int)Math.Ceiling(Bounds.Height * scaling), area.Height);
        var gap = (int)Math.Ceiling(8 * scaling);
        var x = point.X + gap;
        var y = point.Y + gap;
        if (x + width > area.Right) x = point.X - gap - width;
        if (y + height > area.Bottom) y = point.Y - gap - height;
        Position = new PixelPoint(
            Math.Clamp(x, area.X, area.Right - width),
            Math.Clamp(y, area.Y, area.Bottom - height));
    }

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _anchor = null;
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
