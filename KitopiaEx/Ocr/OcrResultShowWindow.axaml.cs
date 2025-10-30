using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using PluginCore;
using PluginCore.ExMethod;
using Ursa.Controls;
using Rect = Avalonia.Rect;

namespace KitopiaEx.Ocr;

public partial class OcrResultShowWindow : UrsaWindow
{
    private bool _inSelectMode = false;
    private ScaleTransform _scaleTransform;
    private Point _startPoint;

    public OcrResultShowWindow()
    {
        InitializeComponent();
        _scaleTransform = new ScaleTransform();


        ItemsControl.RenderTransform = _scaleTransform;
        _scaleTransform.ScaleX = 1d;
        _scaleTransform.ScaleY = 1d;
        ItemsControl.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);
        Image.SizeChanged += OnSizeChanged;
        Image.PropertyChanged += (sender, args) =>
        {
            if (args.Property == Image.SourceProperty)
            {
                if (Image.Source is not null)
                {
                    ItemsControl.Width = Image.Source.Size.Width;
                    ItemsControl.Height = Image.Source.Size.Height;
                    double scale = Image.Bounds.Size.Width / Image.Source.Size.Width;
                    _scaleTransform.ScaleX = scale;
                    _scaleTransform.ScaleY = scale;
                }
            }
        };
    }

    public void UpdateImageScale()
    {
        ItemsControl.Width = Image.Source.Size.Width;
        ItemsControl.Height = Image.Source.Size.Height;
        double scale = Image.Bounds.Size.Width / Image.Source.Size.Width;
        _scaleTransform.ScaleX = scale;
        _scaleTransform.ScaleY = scale;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (Image.Source is null)
        {
            return;
        }

        ItemsControl.Width = Image.Source.Size.Width;
        ItemsControl.Height = Image.Source.Size.Height;
        double scale = Image.Bounds.Size.Width / Image.Source.Size.Width;
        _scaleTransform.ScaleX = scale;
        _scaleTransform.ScaleY = scale;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Image.SizeChanged -= OnSizeChanged;
    }


    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (Image.Source is not null)
        {
            _scaleTransform.ScaleX /= (e.PreviousSize.Width / e.NewSize.Width);
            _scaleTransform.ScaleY /= (e.PreviousSize.Width / e.NewSize.Width);
        }
    }


    private void InputElement_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var bottomRight = e.GetPosition(this);
        if (_inSelectMode)
        {
            ClearAllSelected();
            var left = Math.Min(_startPoint.X, bottomRight.X);
            var top = Math.Min(_startPoint.Y, bottomRight.Y);
            var right = Math.Max(_startPoint.X, bottomRight.X);
            var bottom = Math.Max(_startPoint.Y, bottomRight.Y);
            var startPoint = new Point(left, top);
            var endPoint = new Point(right, bottom);
            var controlsInBounds = ControlHelper.GetControlsInBounds(ItemsControl.ItemsPanelRoot,
                new Rect(startPoint, endPoint));
            foreach (var controlsInBound in controlsInBounds)
            {
                if (controlsInBound is AdaptiveTextBox adaptiveTextBox1)
                {
                    adaptiveTextBox1.SelectText(this.TranslatePoint(startPoint, controlsInBound).Value,
                        this.TranslatePoint(endPoint, controlsInBound).Value);
                }
            }
        }
        else
        {
            var position = bottomRight;
            position = this.TranslatePoint(position, ItemsControl.ItemsPanelRoot).Value;
            var adaptiveTextBox = ItemsControl.ItemsPanelRoot.GetVisualAt<AdaptiveTextBox>(position);
            if (adaptiveTextBox is not null)
            {
                ClearPointerHover();
                adaptiveTextBox.SetPointerIsHover();
            }
            else
            {
                ClearPointerHover();
            }
        }
    }

    private void ClearPointerHover()
    {
        foreach (var itemsControlItem in ItemsControl.GetLogicalChildren())
        {
            if (itemsControlItem.LogicalChildren.First() is AdaptiveTextBox adaptiveTextBox)
            {
                adaptiveTextBox.SetPointerIsNotHover();
            }
        }
    }

    private void ClearAllSelected()
    {
        foreach (var itemsControlItem in ItemsControl.GetLogicalChildren())
        {
            if (itemsControlItem.LogicalChildren.First() is AdaptiveTextBox adaptiveTextBox)
            {
                adaptiveTextBox.ClearSelection();
            }
        }
    }

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ClearAllSelected();
            _inSelectMode = true;
            _startPoint = e.GetPosition(this);
        }
    }

    private void InputElement_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_inSelectMode)
        {
            return;
        }

        _inSelectMode = false;
        var bottomRight = e.GetPosition(this);
        var left = Math.Min(_startPoint.X, bottomRight.X);
        var top = Math.Min(_startPoint.Y, bottomRight.Y);
        var right = Math.Max(_startPoint.X, bottomRight.X);
        var bottom = Math.Max(_startPoint.Y, bottomRight.Y);
        var startPoint = new Point(left, top);
        var endPoint = new Point(right, bottom);
        var controlsInBounds = ControlHelper.GetControlsInBounds(ItemsControl.ItemsPanelRoot,
            new Rect(startPoint, endPoint));
        StringBuilder sb = new StringBuilder();
        List<(Point, AdaptiveTextBox)> list = new List<(Point, AdaptiveTextBox)>();
        foreach (var controlsInBound in controlsInBounds)
        {
            AdaptiveTextBox adaptiveTextBox;
            if (controlsInBound is AdaptiveTextBox adaptiveTextBox1)
            {
                adaptiveTextBox = adaptiveTextBox1;
            }
            else
            {
                continue;
            }

            list.Add((adaptiveTextBox.TopLeft, adaptiveTextBox));
        }

        list.Sort((e, a) =>
        {
            if (Math.Abs(e.Item1.Y - a.Item1.Y) > Double.Epsilon)
            {
                return (int)(e.Item1.Y - a.Item1.Y);
            }
            else
                return (int)(e.Item1.X - a.Item1.X);
        });
        foreach (var (point, item2) in list)
        {
            if (item2.SelectedText == "")
            {
                sb.Append(item2.Text);
            }
            else
                sb.Append(item2.SelectedText);

            sb.Append(Environment.NewLine);
        }

        if (sb.Length == 0)
        {
            return;
        }

        this.Clipboard.SetTextAsync(sb.ToString());
        ClearAllSelected();
        Kitopia.IToastService.Show("已复制", sb.ToString());
    }

    private void InputElement_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        foreach (var itemsControlItem in ItemsControl.GetLogicalChildren())
        {
            if (itemsControlItem.LogicalChildren.First() is AdaptiveTextBox adaptiveTextBox)
            {
                adaptiveTextBox.SetPointerIsNotHover();
            }
        }
    }

    private void InputElement_OnPointerExited(object? sender, PointerEventArgs e)
    {
    }

    private void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
    }
}