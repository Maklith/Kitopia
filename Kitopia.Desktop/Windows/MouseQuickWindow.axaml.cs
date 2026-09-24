using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Kitopia.Desktop.Features.Search.ViewModels;
using Kitopia.Desktop.Features.Services;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
#if WINDOWS
using Kitopia.Desktop.Platform.Windows;
#endif

namespace Kitopia.Desktop.Windows;

public partial class MouseQuickWindow : Window
{
    private const double ImageZoomStep = 0.25;
    private MouseQuickWindowViewModel? _viewModel;
    private readonly MatrixTransform _imageTransform = new();
    private Vector _imageOffset;
    private bool _isApplyingZoom;
    private bool _isImageDragging;
    private Point _imageDragStartPoint;
    private Vector _imageDragStartOffset;
    private IPointer? _imageDragPointer;
    public PixelRect? AnchorBounds { get; set; }
    public nint OriginalWindowHandle { get; set; }

    public MouseQuickWindow()
    {
        InitializeComponent();
        ImagePreview.RenderTransformOrigin = RelativePoint.TopLeft;
        ImagePreview.RenderTransform = _imageTransform;
        ApplyImageTransform(ImageZoom.Value);
        ImageZoom.ValueChanged += ImageZoom_OnValueChanged;
        ImageViewport.SizeChanged += ImageViewport_OnSizeChanged;
        ImageViewport.AddHandler(InputElement.PointerWheelChangedEvent,
            ImageViewport_OnPointerWheelChanged, RoutingStrategies.Tunnel, true);
        ImageViewport.AddHandler(InputElement.PointerPressedEvent,
            ImageViewport_OnPointerPressed, RoutingStrategies.Tunnel, true);
        ImageViewport.AddHandler(InputElement.PointerMovedEvent,
            ImageViewport_OnPointerMoved, RoutingStrategies.Tunnel, true);
        ImageViewport.AddHandler(InputElement.PointerReleasedEvent,
            ImageViewport_OnPointerReleased, RoutingStrategies.Tunnel, true);
        ImageViewport.AddHandler(InputElement.PointerCaptureLostEvent,
            ImageViewport_OnPointerCaptureLost, RoutingStrategies.Bubble, true);
        Focusable = true;
        Deactivated += (_, _) => Close();
        Activated += (_, _) => Focus();
        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null) _viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
            _viewModel = DataContext as MouseQuickWindowViewModel;
            if (_viewModel is not null) _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
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

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MouseQuickWindowViewModel.SelectedPath))
        {
            _isApplyingZoom = true;
            ImageZoom.Value = 1;
            _isApplyingZoom = false;
            _imageOffset = Vector.Zero;
            ApplyImageTransform(1);
            StopImageDrag(_imageDragPointer);
        }
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

    private void ImageViewport_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_viewModel?.PreviewImage is null || e.Delta.Y == 0) return;

        var oldZoom = ImageZoom.Value;
        var newZoom = Math.Clamp(
            oldZoom + Math.Sign(e.Delta.Y) * ImageZoomStep,
            ImageZoom.Minimum,
            ImageZoom.Maximum);
        if (newZoom == oldZoom)
        {
            e.Handled = true;
            return;
        }

        var pointer = e.GetCurrentPoint(ImageViewport).Position;
        if (ImageViewport.TranslatePoint(pointer, ImagePreview) is not { } imagePosition)
        {
            e.Handled = true;
            return;
        }

        _isApplyingZoom = true;
        ImageZoom.Value = newZoom;
        _isApplyingZoom = false;
        ApplyImageTransform(newZoom);
        if (ImagePreview.TranslatePoint(imagePosition, ImageViewport) is { } transformedImagePosition)
        {
            var correction = pointer - transformedImagePosition;
            _imageOffset = ClampImageOffset(_imageOffset + correction, newZoom);
            ApplyImageTransform(newZoom);
        }
        e.Handled = true;
    }

    private void ImageZoom_OnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isApplyingZoom) return;

        var center = ImageViewport.Bounds.Center;
        var oldZoom = e.OldValue > 0 ? e.OldValue : 1;
        var imagePosition = new Point(
            (center.X - _imageOffset.X) / oldZoom,
            (center.Y - _imageOffset.Y) / oldZoom);
        _imageOffset = ClampImageOffset(
            new Vector(
                center.X - imagePosition.X * e.NewValue,
                center.Y - imagePosition.Y * e.NewValue),
            e.NewValue);
        ApplyImageTransform(e.NewValue);
    }

    private void ImageViewport_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _imageOffset = ClampImageOffset(_imageOffset, ImageZoom.Value);
        ApplyImageTransform(ImageZoom.Value);
    }

    private Vector ClampImageOffset(Vector offset, double zoom)
    {
        var viewport = ImageViewport.Bounds.Size;
        return new Vector(
            ClampImageOffset(offset.X, viewport.Width, zoom),
            ClampImageOffset(offset.Y, viewport.Height, zoom));
    }

    private static double ClampImageOffset(double offset, double viewportSize, double zoom)
    {
        if (viewportSize <= 0) return 0;

        var minimum = viewportSize * (1 - zoom);
        return Math.Clamp(offset, minimum, 0);
    }

    private void ApplyImageTransform(double zoom)
    {
        _imageTransform.Matrix = new Matrix(
            zoom, 0,
            0, zoom,
            _imageOffset.X, _imageOffset.Y);
    }

    private void ImageViewport_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel?.PreviewImage is null || !e.GetCurrentPoint(ImageViewport).Properties.IsLeftButtonPressed)
            return;

        var point = e.GetPosition(ImageViewport);
        if (!ImageViewport.Bounds.Contains(point)) return;

        _isImageDragging = true;
        _imageDragPointer = e.Pointer;
        _imageDragStartPoint = point;
        _imageDragStartOffset = _imageOffset;
        e.Pointer.Capture(ImageViewport);
        e.Handled = true;
    }

    private void ImageViewport_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isImageDragging || e.Pointer != _imageDragPointer || e.Pointer.Captured != ImageViewport)
            return;

        var delta = e.GetPosition(ImageViewport) - _imageDragStartPoint;
        _imageOffset = ClampImageOffset(new Vector(
            _imageDragStartOffset.X + delta.X,
            _imageDragStartOffset.Y + delta.Y),
            ImageZoom.Value);
        ApplyImageTransform(ImageZoom.Value);
        e.Handled = true;
    }

    private void ImageViewport_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isImageDragging || e.Pointer != _imageDragPointer ||
            e.InitialPressMouseButton != MouseButton.Left)
            return;

        StopImageDrag(e.Pointer);
        e.Handled = true;
    }

    private void ImageViewport_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) =>
        StopImageDrag(e.Pointer);

    private void StopImageDrag(IPointer? pointer)
    {
        _isImageDragging = false;
        _imageDragPointer = null;
        if (pointer?.Captured == ImageViewport) pointer.Capture(null);
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
        NativePreview.Content = null;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
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
