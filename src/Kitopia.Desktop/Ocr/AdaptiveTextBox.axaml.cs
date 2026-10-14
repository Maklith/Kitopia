using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Kitopia.Desktop.Ocr;

public partial class AdaptiveTextBox : TextBlock
{
    public static readonly StyledProperty<Point> TopLeftProperty =
        AvaloniaProperty.Register<AdaptiveTextBox, Point>(nameof(TopLeft));
    public static readonly StyledProperty<Point> BottomRightProperty =
        AvaloniaProperty.Register<AdaptiveTextBox, Point>(nameof(BottomRight));
    public static readonly StyledProperty<int> SelectionStartProperty =
        TextBox.SelectionStartProperty.AddOwner<AdaptiveTextBox>();
    public static readonly StyledProperty<int> SelectionEndProperty =
        TextBox.SelectionEndProperty.AddOwner<AdaptiveTextBox>();
    public static readonly DirectProperty<AdaptiveTextBox, string> SelectedTextProperty =
        AvaloniaProperty.RegisterDirect<AdaptiveTextBox, string>(nameof(SelectedText), box => box.SelectedText);
    public static readonly StyledProperty<IBrush?> SelectionBrushProperty =
        TextBox.SelectionBrushProperty.AddOwner<AdaptiveTextBox>();

    private bool _canCopy;

    static AdaptiveTextBox()
    {
        FocusableProperty.OverrideDefaultValue<AdaptiveTextBox>(true);
        AffectsRender<AdaptiveTextBox>(SelectionStartProperty, SelectionEndProperty, SelectionBrushProperty);
        BackgroundProperty.OverrideDefaultValue<AdaptiveTextBox>(new SolidColorBrush(Colors.Gray, 0.7));
        ForegroundProperty.OverrideDefaultValue<AdaptiveTextBox>(new SolidColorBrush(Colors.White));
    }

    public Point TopLeft
    {
        get => GetValue(TopLeftProperty);
        set => SetValue(TopLeftProperty, value);
    }

    public Point BottomRight
    {
        get => GetValue(BottomRightProperty);
        set => SetValue(BottomRightProperty, value);
    }

    public int SelectionStart
    {
        get => GetValue(SelectionStartProperty);
        set => SetValue(SelectionStartProperty, value);
    }

    public int SelectionEnd
    {
        get => GetValue(SelectionEndProperty);
        set => SetValue(SelectionEndProperty, value);
    }

    public string SelectedText => GetSelection();

    public IBrush? SelectionBrush
    {
        get => GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    public bool CanCopy
    {
        get => _canCopy;
        private set => SetAndRaise(CanCopyProperty, ref _canCopy, value);
    }

    public static readonly DirectProperty<AdaptiveTextBox, bool> CanCopyProperty =
        AvaloniaProperty.RegisterDirect<AdaptiveTextBox, bool>(nameof(CanCopy), box => box.CanCopy);
    public static readonly RoutedEvent<RoutedEventArgs> CopyingToClipboardEvent =
        RoutedEvent.Register<AdaptiveTextBox, RoutedEventArgs>("CopyingToClipboard", RoutingStrategies.Bubble);

    public event EventHandler<RoutedEventArgs>? CopyingToClipboard
    {
        add => AddHandler(CopyingToClipboardEvent, value);
        remove => RemoveHandler(CopyingToClipboardEvent, value);
    }

    public override void ApplyTemplate()
    {
        base.ApplyTemplate();
        Focusable = false;
        var width = Math.Abs(BottomRight.X - TopLeft.X);
        var height = Math.Abs(BottomRight.Y - TopLeft.Y);
        Width = width;
        Height = height;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        TextAlignment = TextAlignment.Center;
        TextWrapping = TextWrapping.NoWrap;
        SelectionBrush = new SolidColorBrush(Colors.Cyan, 0.7);

        var targetSize = Math.Max(1, height / 1.5);
        var textBlock = new TextBlock { Text = Text, FontSize = targetSize };
        textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        while (targetSize > 1 && textBlock.DesiredSize.Width > width)
        {
            targetSize--;
            textBlock.FontSize = targetSize;
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }

        FontSize = targetSize;
    }

    public async Task Copy()
    {
        if (!CanCopy || string.IsNullOrEmpty(SelectedText))
            return;

        var args = new RoutedEventArgs(CopyingToClipboardEvent);
        RaiseEvent(args);
        if (args.Handled)
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(SelectedText);
    }

    public void SelectAll()
    {
        SetCurrentValue(SelectionStartProperty, 0);
        SetCurrentValue(SelectionEndProperty, Text?.Length ?? 0);
        UpdateCommandStates();
    }

    public void ClearSelection()
    {
        SetCurrentValue(SelectionEndProperty, SelectionStart);
        UpdateCommandStates();
    }

    internal void SelectText(Point start, Point end)
    {
        if (TextLayout is null)
            return;

        SetCurrentValue(SelectionEndProperty, TextLayout.HitTestPoint(end).TextPosition);
        SetCurrentValue(SelectionStartProperty, TextLayout.HitTestPoint(start).TextPosition);
        UpdateCommandStates();
    }

    protected override void RenderTextLayout(DrawingContext context, Point origin)
    {
        if (SelectionStart != SelectionEnd && SelectionBrush is not null)
        {
            var start = Math.Min(SelectionStart, SelectionEnd);
            var length = Math.Abs(SelectionEnd - SelectionStart);
            using (context.PushTransform(Matrix.CreateTranslation((Vector)origin)))
            {
                foreach (var rect in TextLayout.HitTestTextRange(start, length))
                    context.FillRectangle(SelectionBrush, PixelRect.FromRect(rect, 1).ToRect(1));
            }
        }

        base.RenderTextLayout(context, origin);
    }

    internal void SetPointerIsHover() => PseudoClasses.Add(":pointerover");
    internal void SetPointerIsNotHover() => PseudoClasses.Remove(":pointerover");

    private void UpdateCommandStates() => CanCopy = !string.IsNullOrEmpty(GetSelection());

    private string GetSelection()
    {
        var text = Text ?? string.Empty;
        var start = Math.Min(SelectionStart, SelectionEnd);
        var end = Math.Max(SelectionStart, SelectionEnd);
        return start == end || end > text.Length ? string.Empty : text[start..end];
    }
}
