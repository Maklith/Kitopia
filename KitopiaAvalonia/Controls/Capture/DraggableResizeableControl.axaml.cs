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
    private Border _resizeSizeBoxBorder;

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
        content.PointerPressed += ContentOnPointerPressed;
        content.PointerMoved += ContentOnPointerMoved;
        content.PointerReleased += ContentOnPointerReleased;
        content.PointerCaptureLost += ContentOnPointerCaptureLost;
        var border = e.NameScope.Find<Border>("ResizeSizeBoxBorder");
        _resizeSizeBoxBorder = border;
        border.PointerPressed += ResizeSizeBoxBorderOnPointerPressed;
        border.PointerReleased += ResizeSizeBoxBorderOnPointerReleased;
        border.PointerMoved += ResizeSizeBoxBorderOnPointerMoved;
        border.PointerExited += ResizeSizeBoxBorderOnPointerExited;
        border.PointerEntered += ResizeSizeBoxBorderOnPointerEntered;
        border.PointerCaptureLost += ResizeSizeBoxBorderOnPointerCaptureLost;
    }


    #region Border: Resize

    private bool _isResizing;
    private Point _resizeStartPoint;
    private bool _isResizingTop;
    private bool _isResizingBottom;
    private bool _isResizingLeft;
    private bool _isResizingRight;

    private void ResizeSizeBoxBorderOnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled) return;
        Focus();
        if (e.GetCurrentPoint(TopLevel.GetTopLevel(this)).Properties.IsLeftButtonPressed)
        {
            e.Pointer.Capture((IInputElement?)sender);
            _isResizing = true;
            _isResizingTop = false;
            _isResizingBottom = false;
            _isResizingLeft = false;
            _isResizingRight = false;
            _resizeStartPoint = new Point();
            var position = e.GetPosition((Visual?)sender);

            if (position.Y < 3)
            {
                _isResizingTop = true;
                _resizeStartPoint = new Point(_resizeStartPoint.X, _dragTransform.Y + Height);
            }

            if (position.Y > Height - 3)
            {
                _isResizingBottom = true;
                _resizeStartPoint = new Point(_resizeStartPoint.X, _dragTransform.Y);
            }

            if (position.X > Width - 3)
            {
                _isResizingRight = true;
                _resizeStartPoint = new Point(_dragTransform.X, _resizeStartPoint.Y);
            }

            if (position.X < 3)
            {
                _isResizingLeft = true;
                _resizeStartPoint = new Point(_dragTransform.X + Width, _resizeStartPoint.Y);
            }

            this.GetParentOfType<ScreenCaptureWindow>().redoStack.Push(new ScreenCaptureRedoInfo
            {
                EditType = ScreenCaptureEditType.调整大小,
                Target = this,
                startPoint = new Point(_dragTransform.X, _dragTransform.Y),
                Size = DesiredSize,
                Type = 截图工具.矩形
            });
        }
    }

    private void ResizeSizeBoxBorderOnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Handled) return;

        if (_isResizing)
        {
            if (_isResizingTop)
            {
                var h = _resizeStartPoint.Y - e.GetPosition(TopLevel.GetTopLevel(this)).Y;
                if (h > 0)
                {
                    Height = h;
                    _dragTransform.Y = _resizeStartPoint.Y - h;
                }
                else
                {
                    Height = -h;
                    // _dragTransform.Y = _resizeStartPoint.Y - h;
                }
            }

            if (_isResizingBottom)
            {
                var h = e.GetPosition(TopLevel.GetTopLevel(this)).Y - _resizeStartPoint.Y;
                if (h > 0)
                {
                    Height = h;
                }
                else
                {
                    Height = -h;
                    _dragTransform.Y = _resizeStartPoint.Y + h;
                }
            }

            if (_isResizingLeft)
            {
                var w = _resizeStartPoint.X - e.GetPosition(TopLevel.GetTopLevel(this)).X;
                if (w > 0)
                {
                    Width = w;
                    _dragTransform.X = _resizeStartPoint.X - w;
                }
                else
                {
                    Width = -w;
                }
            }

            if (_isResizingRight)
            {
                var w = e.GetPosition(TopLevel.GetTopLevel(this)).X - _resizeStartPoint.X;
                if (w > 0)
                {
                    Width = w;
                }
                else
                {
                    Width = -w;
                    _dragTransform.X = _resizeStartPoint.X + w;
                }
            }

            RaiseEvent(new LocationOrSizeChangedEventArgs
                { Source = this, RoutedEvent = LocationOrSizeChangedEvent });
        }
        else
        {
            var position = e.GetPosition((Visual?)sender);
            UpdatePointerOnResizeSizeBoxBorder(position);
        }
    }

    private void ResizeSizeBoxBorderOnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left) _isResizing = false;
    }

    private void ResizeSizeBoxBorderOnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isResizing = false;
    }

    private void ResizeSizeBoxBorderOnPointerEntered(object? sender, PointerEventArgs e)
    {
        var position = e.GetPosition((Visual?)sender);
        UpdatePointerOnResizeSizeBoxBorder(position);
    }

    private void ResizeSizeBoxBorderOnPointerExited(object? sender, PointerEventArgs e)
    {
        Cursor?.Dispose();
        Cursor = Cursor.Default;
    }

    private void UpdatePointerOnResizeSizeBoxBorder(Point position)
    {
        var top = false;
        var left = false;
        var right = false;
        var bottom = false;

        if (position.Y < 3) top = true;

        if (position.Y > Height - 3) bottom = true;

        if (position.X > Width - 3) right = true;

        if (position.X < 3) left = true;

        if (left && top)
        {
            UpdateCursor("TopLeftCorner");
            return;
        }

        if (right && top)
        {
            UpdateCursor("TopRightCorner");
            return;
        }

        if (left && bottom)
        {
            UpdateCursor("BottomLeftCorner");

            return;
        }

        if (right && bottom)
        {
            UpdateCursor("BottomRightCorner");
            return;
        }

        if (left)
        {
            UpdateCursor("LeftSide");
            return;
        }

        if (right)
        {
            UpdateCursor("RightSide");
            return;
        }

        if (top)
        {
            UpdateCursor("TopSide");
            return;
        }

        if (bottom)
        {
            UpdateCursor("BottomSide");
            return;
        }
    }

    private void UpdateCursor(string cursor)
    {
        if (Cursor == null || Cursor.ToString().Equals(cursor)) return;
        Cursor?.Dispose();
        Cursor = new Cursor(Enum.Parse<StandardCursorType>(cursor));
    }

    #endregion

    #region Internal Control: Dragging

    private void ContentOnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs pointerCaptureLostEventArgs)
    {
        _isDragging = false;
    }

    private void ContentOnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Focus();
        var visualParent = (Canvas)this.GetVisualParent();
        foreach (var canvasChild in visualParent.Children)
            if (canvasChild is CaptureToolBase captureTool)
                captureTool.IsSelected = false;

        IsSelected = true;
        if (e.Handled) return;
        if (e.GetCurrentPoint(TopLevel.GetTopLevel(this)).Properties.IsLeftButtonPressed)
        {
            e.Pointer.Capture((IInputElement?)sender);
            _isDragging = true;
            _dragStartPoint = e.GetPosition(TopLevel.GetTopLevel(this));
            this.GetParentOfType<ScreenCaptureWindow>().redoStack.Push(new ScreenCaptureRedoInfo
            {
                EditType = ScreenCaptureEditType.移动,
                Target = this,
                startPoint = new Point(_dragTransform.X, _dragTransform.Y),
                Type = 截图工具.矩形
            });
        }
    }

    private void ContentOnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Handled) return;

        if (OnlyShowReSizingBoxOnSelect)
            if (!Cursor.ToString().Equals("SizeAll"))
            {
                Cursor?.Dispose();
                Cursor = new Cursor(StandardCursorType.SizeAll);
            }

        if (_isDragging)
        {
            var dragDelta = e.GetPosition(TopLevel.GetTopLevel(this)) - _dragStartPoint;
            _dragStartPoint = e.GetPosition(TopLevel.GetTopLevel(this));
            _dragTransform.X += dragDelta.X;
            _dragTransform.Y += dragDelta.Y;

            RaiseEvent(new LocationOrSizeChangedEventArgs
                { Source = this, RoutedEvent = LocationOrSizeChangedEvent });
        }
    }

    private void ContentOnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Handled) return;
        if (_isDragging && e.InitialPressMouseButton == MouseButton.Left) _isDragging = false;
    }

    #endregion
}