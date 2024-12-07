using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using NodifyM.Avalonia.Controls;

namespace KitopiaAvalonia.Controls.Capture;

public class ArrowLine : Control
{
    public ArrowLine()
    {
        AffectsGeometry<ArrowLine>(SourceProperty, TargetProperty, StrokeProperty, FillProperty,
            StrokeThicknessProperty);
    }

    private static void AffectsGeometryInvalidate(ArrowLine control, AvaloniaPropertyChangedEventArgs e)
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
        where TShape : ArrowLine
    {
        foreach (var property in properties)
            property.Changed.Subscribe(e =>
            {
                if (e.Sender is TShape shape) AffectsGeometryInvalidate(shape, e);
            });
    }

    #region 箭头

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
                    StrokeThickness);

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
                var (arrowStart, arrowEnd) = DrawLineGeometry(context, Source, Target);
                if (ArrowSize.Width != 0d && ArrowSize.Height != 0d) DrawDefaultArrowhead(context, Source, Target);
                context.EndFigure(true);
            }

            return _geometry;
        }
    }

    protected ((Point ArrowStartSource, Point ArrowStartTarget), (Point ArrowEndSource, Point ArrowEndTarget))
        DrawLineGeometry(StreamGeometryContext context, Point source, Point target)
    {
        var headWidth = ArrowSize.Width / 4;
        var headHeight = ArrowSize.Height;
        Vector delta = source - target;
        var angle = Math.Atan2(delta.Y, delta.X);
        var sinT = Math.Sin(angle);
        var cosT = Math.Cos(angle);
        // Console.WriteLine($"angle:{angle}, sinT:{sinT}, cosT:{cosT}");
        var target1 = new Point(target.X + headHeight * cosT, target.Y + headHeight * sinT);

        context.BeginFigure(source, true);
        context.LineTo(new Point(target1.X + headWidth * sinT, target1.Y - headWidth * cosT));
        context.LineTo(new Point(target1.X - headWidth * sinT, target1.Y + headWidth * cosT));
        context.EndFigure(false);

        return ((target, source), (source, target));
    }

    protected void DrawDefaultArrowhead(StreamGeometryContext context, Point source, Point target)
    {
        Vector delta = source - target;
        var headWidth = ArrowSize.Width;
        var headHeight = ArrowSize.Height / 2;

        var angle = Math.Atan2(delta.Y, delta.X);
        var sinT = Math.Sin(angle);
        var cosT = Math.Cos(angle);
        // Console.WriteLine($"angle:{angle}, sinT:{sinT}, cosT:{cosT}");
        var from = new Point(Math.Round(target.X + (headWidth * cosT - headHeight * sinT)),
            Math.Round(target.Y + (headWidth * sinT + headHeight * cosT)));
        var to = new Point(Math.Round(target.X + (headWidth * cosT + headHeight * sinT)),
            Math.Round(target.Y - (headHeight * cosT - headWidth * sinT)));
        //Console.WriteLine($"from:{from}, to:{to}");
        context.BeginFigure(target, true);
        context.LineTo(from);
        context.LineTo(to);
        context.EndFigure(false);
    }

    public static readonly AvaloniaProperty<Point> SourceProperty =
        AvaloniaProperty.Register<ArrowLine, Point>(nameof(Source));

    public static readonly AvaloniaProperty<Point> TargetProperty =
        AvaloniaProperty.Register<ArrowLine, Point>(nameof(Target));

    public static readonly AvaloniaProperty<Size> ArrowSizeProperty =
        AvaloniaProperty.Register<ArrowLine, Size>(nameof(ArrowSize), new Size(8, 8));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<ArrowLine, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<ArrowLine, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<ArrowLine, double>(nameof(StrokeThickness));

    public static readonly StyledProperty<Stretch> StretchProperty =
        AvaloniaProperty.Register<ArrowLine, Stretch>(nameof(Stretch));

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

    #endregion
}