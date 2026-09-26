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
#if WINDOWS
using Kitopia.Desktop.Platform.Windows;
#endif

namespace Kitopia.Desktop.Controls;

public partial class FilePreviewControl : UserControl
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

    public event Action? CloseRequested;

    public FilePreviewControl()
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
        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null) _viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
            NativePreview.Content = null;
            _viewModel = DataContext as MouseQuickWindowViewModel;
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
                UpdateNativePreview();
            }
        };
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
        if (e.PropertyName == nameof(MouseQuickWindowViewModel.NativePath)) UpdateNativePreview();
    }

    private void UpdateNativePreview()
    {
        NativePreview.Content = null;
        if (_viewModel?.NativePath is not { } path) return;
#if WINDOWS
        var preview = new NativeFilePreviewHost { FilePath = path };
        _viewModel.IsLoading = true;
        preview.CloseRequested += () => CloseRequested?.Invoke();
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
        var newZoom = Math.Clamp(oldZoom + Math.Sign(e.Delta.Y) * ImageZoomStep,
            ImageZoom.Minimum, ImageZoom.Maximum);
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
        var imagePosition = new Point((center.X - _imageOffset.X) / oldZoom,
            (center.Y - _imageOffset.Y) / oldZoom);
        _imageOffset = ClampImageOffset(new Vector(
            center.X - imagePosition.X * e.NewValue,
            center.Y - imagePosition.Y * e.NewValue), e.NewValue);
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
        return new Vector(ClampImageOffset(offset.X, viewport.Width, zoom),
            ClampImageOffset(offset.Y, viewport.Height, zoom));
    }

    private static double ClampImageOffset(double offset, double viewportSize, double zoom)
    {
        if (viewportSize <= 0) return 0;
        return Math.Clamp(offset, viewportSize * (1 - zoom), 0);
    }

    private void ApplyImageTransform(double zoom) => _imageTransform.Matrix = new Matrix(
        zoom, 0, 0, zoom, _imageOffset.X, _imageOffset.Y);

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
            _imageDragStartOffset.Y + delta.Y), ImageZoom.Value);
        ApplyImageTransform(ImageZoom.Value);
        e.Handled = true;
    }

    private void ImageViewport_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isImageDragging || e.Pointer != _imageDragPointer ||
            e.InitialPressMouseButton != MouseButton.Left) return;
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
}
