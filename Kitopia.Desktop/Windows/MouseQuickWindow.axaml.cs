using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Kitopia.Desktop.Features.Search.ViewModels;
#if WINDOWS
using Kitopia.Desktop.Platform.Windows;
#endif

namespace Kitopia.Desktop.Windows;

public partial class MouseQuickWindow : Window
{
    private MouseQuickWindowViewModel? _viewModel;
    public PixelRect? AnchorBounds { get; set; }

    public MouseQuickWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) => Close();
        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null) _viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
            _viewModel = DataContext as MouseQuickWindowViewModel;
            if (_viewModel is not null) _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        };
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MouseQuickWindowViewModel.SelectedPath)) ImageZoom.Value = 1;
        if (e.PropertyName != nameof(MouseQuickWindowViewModel.NativePath)) return;
        NativePreview.Content = null;
        if (_viewModel?.NativePath is not { } path) return;
#if WINDOWS
        var preview = new NativeFilePreviewHost { FilePath = path };
        _viewModel.IsLoading = true;
        preview.CloseRequested += Close;
        preview.PreviewReady += () =>
        {
            if (ReferenceEquals(NativePreview.Content, preview)) _viewModel.IsLoading = false;
        };
        preview.PreviewFailed += message => Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(NativePreview.Content, preview)) return;
            NativePreview.Content = null;
            _viewModel.IsLoading = false;
            _viewModel.Message = message;
        });
        try { NativePreview.Content = preview; }
        catch (InvalidOperationException)
        {
            NativePreview.Content = null;
            _viewModel.IsLoading = false;
            _viewModel.Message = "无法加载文件预览组件，可以使用“打开文件”查看。";
        }
#else
        _viewModel.Message = "当前系统暂不支持此类文件预览，可以使用“打开文件”查看。";
#endif
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
        NativePreview.Content = null;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
            _viewModel.Dispose();
        }
        base.OnClosed(e);
    }
}
