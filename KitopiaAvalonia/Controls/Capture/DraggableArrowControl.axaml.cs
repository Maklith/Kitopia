using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Core.Utils;
using KitopiaAvalonia.SDKs;
using KitopiaAvalonia.Windows;

namespace KitopiaAvalonia.Controls.Capture;

public class DraggableArrowControl : CaptureToolBase
{
    public static readonly RoutedEvent<LocationOrSizeChangedEventArgs> LocationOrSizeChangedEvent =
        RoutedEvent.Register<DraggableResizeableControl, LocationOrSizeChangedEventArgs>(nameof(LocationOrSizeChanged),
            RoutingStrategies.Bubble);

    public event EventHandler<LocationOrSizeChangedEventArgs>? LocationOrSizeChanged
    {
        add => AddHandler<LocationOrSizeChangedEventArgs>(LocationOrSizeChangedEvent, value);
        remove => RemoveHandler<LocationOrSizeChangedEventArgs>(LocationOrSizeChangedEvent, value);
    }


    private bool _isDragging;
    private Point _dragStartPoint;

    public DraggableArrowControl()
    {
        Focusable = true;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        var childOfType = this.GetChildOfType<Rectangle>("S");
        childOfType.PointerPressed += PointOnPointerPressed;
        childOfType.PointerMoved += PointOnPointerMoved;
        childOfType.PointerReleased += PointOnPointerReleased;
        childOfType.PointerCaptureLost += PointOnPointerCaptureLost;
        var childOfType2 = this.GetChildOfType<Rectangle>("E");
        childOfType2.PointerPressed += PointOnPointerPressed;
        childOfType2.PointerMoved += PointOnPointerMoved;
        childOfType2.PointerReleased += PointOnPointerReleased;
        childOfType2.PointerCaptureLost += PointOnPointerCaptureLost;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        PointerPressed += ContentOnPointerPressed;
        PointerMoved += ContentOnPointerMoved;
        PointerReleased += ContentOnPointerReleased;
        PointerCaptureLost += ContentOnPointerCaptureLost;
    }


    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        PointerPressed -= ContentOnPointerPressed;
        PointerMoved -= ContentOnPointerMoved;
        PointerReleased -= ContentOnPointerReleased;
        PointerCaptureLost -= ContentOnPointerCaptureLost;
        var childOfType = this.GetChildOfType<Rectangle>("S");
        childOfType.PointerPressed -= PointOnPointerPressed;
        childOfType.PointerMoved -= PointOnPointerMoved;
        childOfType.PointerReleased -= PointOnPointerReleased;
        childOfType.PointerCaptureLost -= PointOnPointerCaptureLost;
        var childOfType2 = this.GetChildOfType<Rectangle>("E");
        childOfType2.PointerPressed -= PointOnPointerPressed;
        childOfType2.PointerMoved -= PointOnPointerMoved;
        childOfType2.PointerReleased -= PointOnPointerReleased;
        childOfType2.PointerCaptureLost -= PointOnPointerCaptureLost;
    }

    #region 修改位置

    //false : 起点
    //true : 终点
    private bool _nowDragPoint;
    private bool _isDragingPoint;

    private void PointOnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Handled) return;
        if (_isDragingPoint && e.InitialPressMouseButton == MouseButton.Left)
        {
            _isDragingPoint = false;
            e.Handled = true;
        }
    }

    private void PointOnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Handled) return;
        if (_isDragingPoint)
        {
            if (_nowDragPoint)
                Target = e.GetPosition(TopLevel.GetTopLevel(this));
            else
                Source = e.GetPosition(TopLevel.GetTopLevel(this));
            e.Handled = true;
        }
    }

    private void PointOnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled) return;
        Focus();
        if (e.GetCurrentPoint(TopLevel.GetTopLevel(this)).Properties.IsLeftButtonPressed)
        {
            e.Pointer.Capture((IInputElement?)sender);
            this.GetParentOfType<ScreenCaptureWindow>().redoStack.Push(new ScreenCaptureRedoInfo
            {
                EditType = ScreenCaptureEditType.移动,
                Target = this,
                Point1 = Source,
                Point2 = Target,
                Type = 截图工具.箭头
            });
            _isDragingPoint = true;
            _nowDragPoint = ((Control)sender).Name != "S";
            e.Handled = true;
        }
    }

    private void PointOnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (e.Handled) return;
        if (_isDragingPoint)
        {
            _isDragingPoint = false;
            e.Handled = true;
        }
    }

    #endregion

    #region 内部控件: 拖拽

    private void ContentOnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled) return;
        Focus();
        var visualParent = (Canvas)this.GetVisualParent();
        foreach (var canvasChild in visualParent.Children)
            if (canvasChild is CaptureToolBase captureTool)
                captureTool.IsSelected = false;

        IsSelected = true;

        if (e.GetCurrentPoint(TopLevel.GetTopLevel(this)).Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            e.Pointer.Capture((IInputElement?)sender);
            _dragStartPoint = e.GetPosition(TopLevel.GetTopLevel(this));
            this.GetParentOfType<ScreenCaptureWindow>().redoStack.Push(new ScreenCaptureRedoInfo
            {
                EditType = ScreenCaptureEditType.移动,
                Target = this,
                Point1 = Source,
                Point2 = Target,
                Type = 截图工具.箭头
            });
        }
    }

    private void ContentOnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Handled) return;
        if (!Cursor.ToString().Equals("SizeAll"))
        {
            Cursor?.Dispose();
            Cursor = new Cursor(StandardCursorType.SizeAll);
        }

        if (_isDragging)
        {
            var dragDelta = e.GetPosition(TopLevel.GetTopLevel(this)) - _dragStartPoint;
            _dragStartPoint = e.GetPosition(TopLevel.GetTopLevel(this));
            Source += dragDelta;
            Target += dragDelta;

            RaiseEvent(new LocationOrSizeChangedEventArgs
                { Source = this, RoutedEvent = LocationOrSizeChangedEvent });
        }
    }

    private void ContentOnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Handled) return;
        if (_isDragging && e.InitialPressMouseButton == MouseButton.Left) _isDragging = false;
    }

    private void ContentOnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_isDragging) _isDragging = false;
    }

    #endregion

    public static readonly AvaloniaProperty<Point> SourceProperty =
        AvaloniaProperty.Register<DraggableArrowControl, Point>(nameof(Source));

    public static readonly AvaloniaProperty<Point> TargetProperty =
        AvaloniaProperty.Register<DraggableArrowControl, Point>(nameof(Target));

    public static readonly AvaloniaProperty<Size> ArrowSizeProperty =
        AvaloniaProperty.Register<DraggableArrowControl, Size>(nameof(ArrowSize), new Size(8, 8));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<DraggableArrowControl, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<DraggableArrowControl, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<DraggableArrowControl, double>(nameof(StrokeThickness));

    public static readonly StyledProperty<Stretch> StretchProperty =
        AvaloniaProperty.Register<DraggableArrowControl, Stretch>(nameof(Stretch));

    public Stretch Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Point Source
    {
        get => (Point)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Point Target
    {
        get => (Point)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    public Size ArrowSize
    {
        get => (Size)GetValue(ArrowSizeProperty);
        set => SetValue(ArrowSizeProperty, value);
    }
}