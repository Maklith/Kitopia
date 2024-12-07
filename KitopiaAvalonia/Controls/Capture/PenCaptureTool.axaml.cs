using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;
using KitopiaAvalonia.SDKs;
using KitopiaAvalonia.Tools;
using KitopiaAvalonia.Windows;
using Pen = System.Windows.Media.Pen;

namespace KitopiaAvalonia.Controls.Capture;

public class PenCaptureTool : CaptureToolBase
{
    private Polyline _currentPolyline;

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<PenCaptureTool, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<PenCaptureTool, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<PenCaptureTool, double>(nameof(StrokeThickness));

    public static readonly StyledProperty<ObservableCollection<Point>> PointsProperty =
        AvaloniaProperty.Register<PenCaptureTool, ObservableCollection<Point>>(nameof(Points));

    public ObservableCollection<Point> Points
    {
        get => (ObservableCollection<Point>)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
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


    public PenCaptureTool()
    {
        Points = new ObservableCollection<Point>();
        Points.CollectionChanged += Update;
        AffectsGeometry<PenCaptureTool>(PointsProperty, StrokeProperty, FillProperty, StrokeThicknessProperty);
        Focusable = true;
    }

    public void Update(object? sender, NotifyCollectionChangedEventArgs notifyCollectionChangedEventArgs)
    {
        InvalidateGeometry();
    }

    private static void AffectsGeometryInvalidate(PenCaptureTool control, AvaloniaPropertyChangedEventArgs e)
    {
        // If the geometry is invalidated when Bounds changes, only invalidate when the Size
        // portion changes.
        if (e.Property == BoundsProperty)
        {
            var oldBounds = (Rect)e.OldValue!;
            var newBounds = (Rect)e.NewValue!;

            if (oldBounds.Size == newBounds.Size) return;
        }

        control.InvalidateGeometry();
    }

    protected void InvalidateGeometry()
    {
        _renderedGeometry = null;
        _definingGeometry = null;

        InvalidateMeasure();
    }

    protected static void AffectsGeometry<TShape>(params AvaloniaProperty[] properties)
        where TShape : PenCaptureTool
    {
        foreach (var property in properties)
            property.Changed.Subscribe(e =>
            {
                if (e.Sender is TShape shape) AffectsGeometryInvalidate(shape, e);
            });
    }

    private Matrix _transform = Matrix.Identity;
    private Geometry? _definingGeometry;
    private Geometry? _renderedGeometry;

    public Geometry? DefiningGeometry
    {
        get
        {
            if (_definingGeometry == null) _definingGeometry = CreateDefiningGeometry();

            return _definingGeometry;
        }
    }

    public Geometry? RenderedGeometry
    {
        get
        {
            if (_renderedGeometry == null && DefiningGeometry != null)
            {
                if (_transform == Matrix.Identity)
                {
                    _renderedGeometry = DefiningGeometry;
                }
                else
                {
                    _renderedGeometry = DefiningGeometry.Clone();

                    if (_renderedGeometry.Transform == null ||
                        _renderedGeometry.Transform.Value == Matrix.Identity)
                        _renderedGeometry.Transform = new MatrixTransform(_transform);
                    else
                        _renderedGeometry.Transform = new MatrixTransform(
                            _renderedGeometry.Transform.Value * _transform);
                }
            }

            return _renderedGeometry;
        }
    }

    public sealed override void Render(DrawingContext context)
    {
        base.Render(context);
        var geometry = RenderedGeometry;

        if (geometry != null)
        {
            var stroke = Stroke;

            ImmutablePen? pen = null;

            if (stroke != null)
                pen = new ImmutablePen(
                    stroke.ToImmutable(),
                    StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

            context.DrawGeometry(Fill, pen, geometry);
        }
    }

    public Geometry CreateDefiningGeometry()
    {
        {
            var _geometry = new StreamGeometry();
            using (var context = _geometry.Open())
            {
                context.SetFillRule(FillRule.EvenOdd);
                // context.BeginFigure(Points.FirstOrDefault(),false);
                // for (var index = 2; index < Points.Count; index++)
                // {
                //     context.QuadraticBezierTo(  Points[index-1],Points[index]);
                // }
                context.BeginFigure(Points.FirstOrDefault(), false);
                for (var index = 0; index < Points.Count; index++)
                {
                    var point = Points[index];
                    if (point.Y <= 0)
                    {
                        context.EndFigure(false);
                        if (index + 1 < Points.Count) context.BeginFigure(Points[index + 1], false);

                        continue;
                    }

                    context.LineTo(point);
                }

                context.EndFigure(false);
            }

            return _geometry;
        }
    }

    #region 内部控件: 拖拽

    private bool _isDragging;
    private Point _dragStartPoint;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled) return;
        var visualParent = (Canvas)this.GetVisualParent();
        foreach (var canvasChild in visualParent.Children)
            if (canvasChild is CaptureToolBase captureTool)
                captureTool.IsSelected = false;

        IsSelected = true;

        if (e.GetCurrentPoint(TopLevel.GetTopLevel(this)).Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            var points = new List<Point>();
            foreach (var point in Points) points.Add(point);
            this.GetParentOfType<ScreenCaptureWindow>().redoStack.Push(new ScreenCaptureRedoInfo()
            {
                EditType = ScreenCaptureEditType.移动,
                Target = this,
                points = points,
                Type = 截图工具.批准
            });
            e.Pointer.Capture(this);
            _dragStartPoint = e.GetPosition(TopLevel.GetTopLevel(this));
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
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
            for (var index = 0; index < Points.Count; index++) Points[index] += dragDelta;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Handled) return;
        if (_isDragging && e.InitialPressMouseButton == MouseButton.Left) _isDragging = false;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_isDragging) _isDragging = false;
    }

    #endregion
}