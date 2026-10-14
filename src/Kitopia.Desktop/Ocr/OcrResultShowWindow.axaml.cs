using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using PluginCore;
using PluginCore.ExMethod;
using Ursa.Controls;
using Point = Avalonia.Point;
using Rect = Avalonia.Rect;

namespace Kitopia.Desktop.Ocr;

public partial class OcrResultShowWindow : UrsaWindow
{
    private readonly ScaleTransform _scaleTransform = new();
    private bool _inSelectMode;
    private Point _startPoint;

    public OcrResultShowWindow()
    {
        InitializeComponent();
        ItemsControl.RenderTransform = _scaleTransform;
        ItemsControl.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);
        Image.SizeChanged += OnSizeChanged;
        Image.PropertyChanged += (_, args) =>
        {
            if (args.Property == Image.SourceProperty)
                UpdateImageScale();
        };
    }

    public static void ShowForCapture(ScreenCaptureResult captureResult)
    {
        if (captureResult.Source is not { IsDisposed: false } source)
            return;

        try
        {
            _ = RecognizeAndShowAsync(source.Clone());
        }
        catch (Exception exception)
        {
            _ = ShowToastAsync("文字提取失败", exception.Message, NotificationType.Error);
        }
    }

    public void SetData(Mat image, IReadOnlyList<OcrTextRegion> regions)
    {
        Image.Source = image.ToAWriteableBitmap();
        ItemsControl.ItemsSource = regions.Select(region => new OcrResult(
            region.Text,
            new Point(region.Left, region.Top),
            new Point(region.Left + region.Width, region.Top + region.Height))).ToArray();
        UpdateImageScale();
    }

    private static async Task RecognizeAndShowAsync(Mat image)
    {
        try
        {
            using (image)
            {
                var services = ServiceManager.Services;
                var ocr = services?.GetService<PluginCore.IOcrService>();
                if (ocr is null || !ocr.IsAvailable)
                {
                    await ShowToastAsync("文字提取", "本地 OCR 模型不可用。", NotificationType.Warning);
                    return;
                }

                var regions = await ocr.RecognizeAsync(image);
                if (regions.Count == 0)
                {
                    await ShowToastAsync("文字提取", "未识别到文字。", NotificationType.Information);
                    return;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var window = new OcrResultShowWindow();
                    window.SetData(image, regions);
                    window.Show();
                });
            }
        }
        catch (Exception exception)
        {
            await ShowToastAsync("文字提取失败", exception.InnerException?.Message ?? exception.Message,
                NotificationType.Error);
        }
    }

    private static Task ShowToastAsync(string title, string message, NotificationType type) =>
        ServiceManager.Services?.GetService<IToastService>()?.Show(title, message, type) ?? Task.CompletedTask;

    public void UpdateImageScale()
    {
        if (Image.Source is null || Image.Source.Size.Width <= 0)
            return;

        ItemsControl.Width = Image.Source.Size.Width;
        ItemsControl.Height = Image.Source.Size.Height;
        var scale = Image.Bounds.Size.Width / Image.Source.Size.Width;
        _scaleTransform.ScaleX = scale;
        _scaleTransform.ScaleY = scale;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateImageScale();

    private void InputElement_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var end = e.GetPosition(this);
        if (_inSelectMode)
        {
            ClearAllSelected();
            SelectInBounds(new Rect(
                new Point(Math.Min(_startPoint.X, end.X), Math.Min(_startPoint.Y, end.Y)),
                new Point(Math.Max(_startPoint.X, end.X), Math.Max(_startPoint.Y, end.Y))));
            return;
        }

        var position = this.TranslatePoint(end, ItemsControl.ItemsPanelRoot);
        if (position is not { } point)
            return;

        var textBox = ItemsControl.ItemsPanelRoot.GetVisualAt<AdaptiveTextBox>(point);
        ClearPointerHover();
        textBox?.SetPointerIsHover();
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
            return;

        _inSelectMode = false;
        var end = e.GetPosition(this);
        var bounds = new Rect(
            new Point(Math.Min(_startPoint.X, end.X), Math.Min(_startPoint.Y, end.Y)),
            new Point(Math.Max(_startPoint.X, end.X), Math.Max(_startPoint.Y, end.Y)));
        var selected = ControlHelper.GetControlsInBounds(ItemsControl.ItemsPanelRoot, bounds)
            .OfType<AdaptiveTextBox>()
            .OrderBy(box => box.TopLeft.Y)
            .ThenBy(box => box.TopLeft.X)
            .ToArray();
        var text = string.Join(Environment.NewLine,
            selected.Select(box => string.IsNullOrEmpty(box.SelectedText) ? box.Text : box.SelectedText));
        if (!string.IsNullOrEmpty(text))
        {
            _ = Clipboard.SetTextAsync(text);
            _ = ShowToastAsync("已复制", text, NotificationType.Information);
        }

        ClearAllSelected();
    }

    private void SelectInBounds(Rect bounds)
    {
        foreach (var textBox in ControlHelper.GetControlsInBounds(ItemsControl.ItemsPanelRoot, bounds).OfType<AdaptiveTextBox>())
        {
            var start = this.TranslatePoint(bounds.TopLeft, textBox);
            var end = this.TranslatePoint(bounds.BottomRight, textBox);
            if (start is { } startPoint && end is { } endPoint)
                textBox.SelectText(startPoint, endPoint);
        }
    }

    private void ClearPointerHover()
    {
        foreach (var textBox in ItemsControl.GetLogicalChildren()
                     .SelectMany(item => item.LogicalChildren.OfType<AdaptiveTextBox>()))
            textBox.SetPointerIsNotHover();
    }

    private void ClearAllSelected()
    {
        foreach (var textBox in ItemsControl.GetLogicalChildren()
                     .SelectMany(item => item.LogicalChildren.OfType<AdaptiveTextBox>()))
            textBox.ClearSelection();
    }

    private void InputElement_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => ClearPointerHover();

    private void Button_OnClick(object? sender, RoutedEventArgs e) => Topmost = !Topmost;
}
