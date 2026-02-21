using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Core.Utils;
using KitopiaAvalonia.SDKs;
using KitopiaAvalonia.Windows;

namespace KitopiaAvalonia.Controls.Capture;

public class LocationOrSizeChangedEventArgs : RoutedEventArgs
{
}

public class DraggableResizeableControl : CaptureToolBase
{
    public static readonly AvaloniaProperty StartTranslateTransformProperty =
        AvaloniaProperty.Register<DraggableResizeableControl, TranslateTransform>("_dragTransform");

    public static readonly AvaloniaProperty OnlyShowReSizingBoxOnSelectProperty =
        AvaloniaProperty.Register<DraggableResizeableControl, bool>(nameof(OnlyShowReSizingBoxOnSelect), true);

    public static readonly RoutedEvent<LocationOrSizeChangedEventArgs> LocationOrSizeChangedEvent =
        RoutedEvent.Register<DraggableResizeableControl, LocationOrSizeChangedEventArgs>(nameof(LocationOrSizeChanged),
            RoutingStrategies.Bubble);

    public event EventHandler<LocationOrSizeChangedEventArgs>? LocationOrSizeChanged
    {
        add => AddHandler<LocationOrSizeChangedEventArgs>(LocationOrSizeChangedEvent, value);
        remove => RemoveHandler<LocationOrSizeChangedEventArgs>(LocationOrSizeChangedEvent, value);
    }

    public TranslateTransform _dragTransform
    {
        get => (TranslateTransform)GetValue(StartTranslateTransformProperty);
        set => SetValue(StartTranslateTransformProperty, value);
    }

    public bool OnlyShowReSizingBoxOnSelect
    {
        get => (bool)GetValue(OnlyShowReSizingBoxOnSelectProperty);
        set => SetValue(OnlyShowReSizingBoxOnSelectProperty, value);
    }

    private bool _isDragging;
    private Point _dragStartPoint;

    public DraggableResizeableControl()
    {
        _dragTransform = new TranslateTransform();
        Focusable = true;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        var content = e.NameScope.Find<ContentPresenter>("Presenter");

        RenderTransform = _dragTransform;
        
        if (content != null)
        {
            content.PointerPressed += ContentOnPointerPressed;
            content.PointerMoved += ContentOnPointerMoved;
            content.PointerReleased += ContentOnPointerReleased;
            content.PointerCaptureLost += ContentOnPointerCaptureLost;
        }

        void AttachThumb(string name, EventHandler<VectorEventArgs> dragDeltaHandler)
        {
            var thumb = e.NameScope.Find<Thumb>(name);
            if (thumb != null)
            {
                thumb.DragStarted += Thumb_DragStarted;
                thumb.DragDelta += dragDeltaHandler;
            }
        }

        AttachThumb("ThumbTL", ThumbTL_DragDelta);
        AttachThumb("ThumbTC", ThumbTC_DragDelta);
        AttachThumb("ThumbTR", ThumbTR_DragDelta);
        AttachThumb("ThumbLC", ThumbLC_DragDelta);
        AttachThumb("ThumbRC", ThumbRC_DragDelta);
        AttachThumb("ThumbBL", ThumbBL_DragDelta);
        AttachThumb("ThumbBC", ThumbBC_DragDelta);
        AttachThumb("ThumbBR", ThumbBR_DragDelta);
    }

    #region Thumbs: Resize

    private void Thumb_DragStarted(object? sender, VectorEventArgs e)
    {
        Focus();
        if (Name != "SelectBox")
        {
            this.GetParentOfType<ScreenCaptureWindow>()?.RedoStack.Push(new ScreenCaptureRedoInfo
            {
                EditType = ScreenCaptureEditType.调整大小,
                Target = this,
                StartPoint = new Point(_dragTransform.X, _dragTransform.Y),
                Size = DesiredSize,
                Type = 截图工具.矩形
            });
        }
    }

    private void UpdateSizeAndPosition(double deltaX, double deltaY, bool updateLeft, bool updateTop, bool updateRight, bool updateBottom)
    {
        double newWidth = Width;
        double newHeight = Height;
        double newX = _dragTransform.X;
        double newY = _dragTransform.Y;

        if (updateLeft)
        {
            newWidth -= deltaX;
            if (newWidth > 0)
            {
                newX += deltaX;
            }
            else
            {
                newWidth = 0;
            }
        }
        else if (updateRight)
        {
            newWidth += deltaX;
            if (newWidth < 0) newWidth = 0;
        }

        if (updateTop)
        {
            newHeight -= deltaY;
            if (newHeight > 0)
            {
                newY += deltaY;
            }
            else
            {
                newHeight = 0;
            }
        }
        else if (updateBottom)
        {
            newHeight += deltaY;
            if (newHeight < 0) newHeight = 0;
        }

        if (newWidth >= 0)
        {
            Width = newWidth;
            _dragTransform.X = newX;
        }
        if (newHeight >= 0)
        {
            Height = newHeight;
            _dragTransform.Y = newY;
        }

        RaiseEvent(new LocationOrSizeChangedEventArgs { Source = this, RoutedEvent = LocationOrSizeChangedEvent });
    }

    private void ThumbTL_DragDelta(object? sender, VectorEventArgs e) => UpdateSizeAndPosition(e.Vector.X, e.Vector.Y, true, true, false, false);
    private void ThumbTC_DragDelta(object? sender, VectorEventArgs e) => UpdateSizeAndPosition(0, e.Vector.Y, false, true, false, false);
    private void ThumbTR_DragDelta(object? sender, VectorEventArgs e) => UpdateSizeAndPosition(e.Vector.X, e.Vector.Y, false, true, true, false);
    private void ThumbLC_DragDelta(object? sender, VectorEventArgs e) => UpdateSizeAndPosition(e.Vector.X, 0, true, false, false, false);
    private void ThumbRC_DragDelta(object? sender, VectorEventArgs e) => UpdateSizeAndPosition(e.Vector.X, 0, false, false, true, false);
    private void ThumbBL_DragDelta(object? sender, VectorEventArgs e) => UpdateSizeAndPosition(e.Vector.X, e.Vector.Y, true, false, false, true);
    private void ThumbBC_DragDelta(object? sender, VectorEventArgs e) => UpdateSizeAndPosition(0, e.Vector.Y, false, false, false, true);
    private void ThumbBR_DragDelta(object? sender, VectorEventArgs e) => UpdateSizeAndPosition(e.Vector.X, e.Vector.Y, false, false, true, true);

    #endregion

    #region Internal Control: Dragging

    private void ContentOnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs pointerCaptureLostEventArgs)
    {
        _isDragging = false;
    }

    private void ContentOnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Focus();
        var visualParent = (Canvas?)this.GetVisualParent();
        if (visualParent != null)
        {
            foreach (var canvasChild in visualParent.Children)
                if (canvasChild is CaptureToolBase captureTool)
                    captureTool.IsSelected = false;
        }

        IsSelected = true;
        if (e.Handled) return;
        
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null && e.GetCurrentPoint(topLevel).Properties.IsLeftButtonPressed)
        {
            e.Pointer.Capture((IInputElement?)sender);
            _isDragging = true;
            _dragStartPoint = e.GetPosition(topLevel);
            if (Name != "SelectBox")
            {
                this.GetParentOfType<ScreenCaptureWindow>()?.RedoStack.Push(new ScreenCaptureRedoInfo
                {
                    EditType = ScreenCaptureEditType.移动,
                    Target = this,
                    StartPoint = new Point(_dragTransform.X, _dragTransform.Y),
                    Type = 截图工具.矩形
                });
            }
        }
    }

    private void ContentOnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Handled) return;

        if (OnlyShowReSizingBoxOnSelect)
        {
            if (Cursor == null || !Cursor.ToString().Equals("SizeAll"))
            {
                Cursor?.Dispose();
                Cursor = new Cursor(StandardCursorType.SizeAll);
            }
        }

        if (_isDragging)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
            {
                var currentPoint = e.GetPosition(topLevel);
                var dragDelta = currentPoint - _dragStartPoint;
                _dragStartPoint = currentPoint;
                _dragTransform.X += dragDelta.X;
                _dragTransform.Y += dragDelta.Y;

                RaiseEvent(new LocationOrSizeChangedEventArgs
                    { Source = this, RoutedEvent = LocationOrSizeChangedEvent });
            }
        }
    }

    private void ContentOnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Handled) return;
        if (_isDragging && e.InitialPressMouseButton == MouseButton.Left) _isDragging = false;
    }

    #endregion
}
