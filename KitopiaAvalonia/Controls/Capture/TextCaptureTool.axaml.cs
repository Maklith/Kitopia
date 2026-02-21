using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Core.Utils;
using KitopiaAvalonia.SDKs;
using KitopiaAvalonia.Windows;

namespace KitopiaAvalonia.Controls.Capture;

public class TextCaptureTool : CaptureToolBase
{
    public bool IsRedoing;

    //Text属性
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<TextCaptureTool, string>(nameof(Text));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public TextCaptureTool()
    {
        _dragTransform = new TranslateTransform();
        RenderTransform = _dragTransform;

        Focusable = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        TextProperty.Changed.Subscribe(TextChange);
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
    }

    private void TextChange(AvaloniaPropertyChangedEventArgs<string> e)
    {
        if (e.Sender == this)
        {
            if (IsRedoing)
            {
                IsRedoing = false;
                return;
            }

            if (this.GetParentOfType<ScreenCaptureWindow>().RedoStack.TryPeek(out var result))
            {
                if (result.Type != 截图工具.文本)
                {
                    this.GetParentOfType<ScreenCaptureWindow>().RedoStack.Push(new ScreenCaptureRedoInfo
                    {
                        EditType = ScreenCaptureEditType.调整大小,
                        Target = this,
                        Type = 截图工具.文本,
                        Data = e.OldValue.Value
                    });
                }
            }
            else
            {
                this.GetParentOfType<ScreenCaptureWindow>().RedoStack.Push(new ScreenCaptureRedoInfo
                {
                    EditType = ScreenCaptureEditType.调整大小,
                    Target = this,
                    Type = 截图工具.文本,
                    Data = e.OldValue.Value
                });
            }
        }
    }


    public static readonly AvaloniaProperty StartTranslateTransformProperty =
        AvaloniaProperty.Register<DraggableResizeableControl, TranslateTransform>("_dragTransform");

    public TranslateTransform _dragTransform
    {
        get => (TranslateTransform)GetValue(StartTranslateTransformProperty);
        set => SetValue(StartTranslateTransformProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
    }

    #region Internal Control: Dragging

    private bool _isDragging;
    private Point _dragStartPoint;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled) return;

        // Make sure clicking the text box area correctly focuses and selects this tool
        Focus();
        
        var visualParent = (Canvas?)this.GetVisualParent();
        if (visualParent != null)
        {
            foreach (var canvasChild in visualParent.Children)
                if (canvasChild is CaptureToolBase captureTool)
                    captureTool.IsSelected = false;
        }

        IsSelected = true;

        if (e.GetCurrentPoint(TopLevel.GetTopLevel(this)).Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            e.Pointer.Capture(this);
            _dragStartPoint = e.GetPosition(TopLevel.GetTopLevel(this));
        }
        e.Handled = true;
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
            _dragTransform.X += dragDelta.X;
            _dragTransform.Y += dragDelta.Y;
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