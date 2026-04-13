using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using Core.ViewModel.Pages.device;

namespace KitopiaAvalonia.Pages;

public partial class DeviceCommunicationPage : UserControl
{
    private DeviceCommunicationPageViewModel? _boundViewModel;

    public DeviceCommunicationPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _boundViewModel = null;
        }

        _boundViewModel = DataContext as DeviceCommunicationPageViewModel;
        if (_boundViewModel is not null)
        {
            _boundViewModel.PropertyChanged += OnViewModelPropertyChanged;
            ScrollToLatest();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DeviceCommunicationPageViewModel.MessageListVersion) or nameof(DeviceCommunicationPageViewModel.CurrentMessages))
        {
            ScrollToLatest();
        }
    }

    private void ScrollToLatest()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var scrollViewer = this.FindControl<ScrollViewer>("ConversationScrollViewer") ?? this.FindDescendantOfType<ScrollViewer>();
            scrollViewer?.ScrollToEnd();
        }, DispatcherPriority.Background);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _boundViewModel = null;
        }
    }

    private void OnPreviewImagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: DeviceChatMessageItem messageItem })
        {
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ShowImagePreviewWindow(messageItem);
            e.Handled = true;
        }
    }

    private void OnPreviewImageZoomClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: DeviceChatMessageItem messageItem })
        {
            return;
        }

        ShowImagePreviewWindow(messageItem);
    }

    private void ShowImagePreviewWindow(DeviceChatMessageItem messageItem)
    {
        if (messageItem.ImagePreview is null)
        {
            return;
        }

        var imageControl = new Image
        {
            Source = messageItem.ImagePreview,
            Stretch = Stretch.Fill,
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        var imageWidth = Math.Max(1d, messageItem.ImagePreview.PixelSize.Width);
        var imageHeight = Math.Max(1d, messageItem.ImagePreview.PixelSize.Height);

        var currentScale = 1d;
        var minScale = 1d;
        const double maxScale = 8d;
        double offsetX = 0d;
        double offsetY = 0d;
        var isPanning = false;
        Point panStartPoint = default;
        double panStartOffsetX = 0d;
        double panStartOffsetY = 0d;
        const double zoomStep = 0.1;

        var previewCanvas = new Canvas
        {
            ClipToBounds = true
        };
        previewCanvas.Children.Add(imageControl);

        var viewport = new Border
        {
            ClipToBounds = true,
            Background = Brushes.Transparent,
            Child = previewCanvas
        };

        var previewWindow = new Window
        {
            Title = "图片预览 100%",
            Width = 980,
            Height = 720,
            MinWidth = 520,
            MinHeight = 420
        };

        void UpdatePreviewTitle()
        {
            var zoomPercent = currentScale / minScale * 100d;
            previewWindow.Title = $"图片预览 {zoomPercent:0}%";
        }

        void ApplyLayout()
        {
            var viewportWidth = previewCanvas.Bounds.Width;
            var viewportHeight = previewCanvas.Bounds.Height;
            if (viewportWidth <= 0 || viewportHeight <= 0)
            {
                return;
            }

            var scaledWidth = imageWidth * currentScale;
            var scaledHeight = imageHeight * currentScale;

            if (scaledWidth <= viewportWidth)
            {
                offsetX = (viewportWidth - scaledWidth) / 2d;
            }
            else
            {
                var minOffsetX = viewportWidth - scaledWidth;
                offsetX = Math.Clamp(offsetX, minOffsetX, 0d);
            }

            if (scaledHeight <= viewportHeight)
            {
                offsetY = (viewportHeight - scaledHeight) / 2d;
            }
            else
            {
                var minOffsetY = viewportHeight - scaledHeight;
                offsetY = Math.Clamp(offsetY, minOffsetY, 0d);
            }

            imageControl.Width = scaledWidth;
            imageControl.Height = scaledHeight;
            Canvas.SetLeft(imageControl, offsetX);
            Canvas.SetTop(imageControl, offsetY);
        }

        void RecalculateScaleForViewport(bool resetZoom)
        {
            var viewportWidth = previewCanvas.Bounds.Width;
            var viewportHeight = previewCanvas.Bounds.Height;
            if (viewportWidth <= 0 || viewportHeight <= 0)
            {
                return;
            }

            var fitScale = Math.Min(viewportWidth / imageWidth, viewportHeight / imageHeight);
            if (fitScale <= 0 || double.IsNaN(fitScale) || double.IsInfinity(fitScale))
            {
                return;
            }

            minScale = fitScale;
            if (resetZoom || currentScale < minScale)
            {
                currentScale = minScale;
            }

            ApplyLayout();
            UpdatePreviewTitle();
        }

        void OnPreviewPointerWheelChanged(object? _, PointerWheelEventArgs e)
        {
            var wheelDelta = e.Delta.Y;
            if (Math.Abs(wheelDelta) <= double.Epsilon)
            {
                return;
            }

            var factor = wheelDelta > 0 ? 1d + zoomStep : 1d - zoomStep;
            var newScale = Math.Clamp(currentScale * factor, minScale, maxScale);
            if (Math.Abs(newScale - currentScale) <= double.Epsilon)
            {
                return;
            }

            var pointer = e.GetPosition(previewCanvas);
            var relativeX = (pointer.X - offsetX) / currentScale;
            var relativeY = (pointer.Y - offsetY) / currentScale;

            currentScale = newScale;
            offsetX = pointer.X - relativeX * currentScale;
            offsetY = pointer.Y - relativeY * currentScale;

            ApplyLayout();
            UpdatePreviewTitle();
            e.Handled = true;
        }

        void OnPreviewPointerPressed(object? _, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(imageControl).Properties.IsLeftButtonPressed)
            {
                return;
            }

            isPanning = true;
            panStartPoint = e.GetPosition(previewCanvas);
            panStartOffsetX = offsetX;
            panStartOffsetY = offsetY;
            imageControl.Cursor = new Cursor(StandardCursorType.SizeAll);
            e.Pointer.Capture(imageControl);
            e.Handled = true;
        }

        void OnPreviewPointerMoved(object? _, PointerEventArgs e)
        {
            if (!isPanning)
            {
                return;
            }

            var currentPoint = e.GetPosition(previewCanvas);
            var delta = currentPoint - panStartPoint;
            offsetX = panStartOffsetX + delta.X;
            offsetY = panStartOffsetY + delta.Y;
            ApplyLayout();
            e.Handled = true;
        }

        void EndPan()
        {
            isPanning = false;
            imageControl.Cursor = new Cursor(StandardCursorType.Hand);
        }

        void OnPreviewPointerReleased(object? _, PointerReleasedEventArgs e)
        {
            if (!isPanning)
            {
                return;
            }

            EndPan();
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        void OnPreviewPointerCaptureLost(object? _, PointerCaptureLostEventArgs e)
        {
            if (!isPanning)
            {
                return;
            }

            EndPan();
            e.Handled = true;
        }

        previewCanvas.PointerWheelChanged += OnPreviewPointerWheelChanged;
        previewCanvas.SizeChanged += (_, _) => RecalculateScaleForViewport(resetZoom: false);
        imageControl.PointerPressed += OnPreviewPointerPressed;
        imageControl.PointerMoved += OnPreviewPointerMoved;
        imageControl.PointerReleased += OnPreviewPointerReleased;
        imageControl.PointerCaptureLost += OnPreviewPointerCaptureLost;

        previewWindow.Content = new Grid
        {
            Margin = new Thickness(12),
            Children =
            {
                viewport
            }
        };

        RecalculateScaleForViewport(resetZoom: true);

        var desktop = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var owner = desktop?.MainWindow;
        if (owner is not null)
        {
            previewWindow.Show(owner);
            return;
        }

        previewWindow.Show();
    }
}
