using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Core.SDKs.Services;
using Core.SDKs.Services.Config;
using Core.SDKs.Tools.ImageTools;
using KitopiaAvalonia.Controls.Capture;
using KitopiaAvalonia.SDKs;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Point = Avalonia.Point;
using Rectangle = SixLabors.ImageSharp.Rectangle;
using Size = Avalonia.Size;

namespace KitopiaAvalonia.Windows;

public partial class ScreenCaptureWindow : Window
{
    bool IsSelected = false;
    private bool Selecting = false;
    private bool PointerOver = false;
    private Point _startPoint;
    public Stack<ScreenCaptureRedoInfo> redoStack = new();
    private List<CaptureToolBase> tools = new();
    private bool selectMode = false;
    private Action<ScreenCaptureInfo> selectModeAction;
    private ScreenCaptureInfo _screenCaptureInfo;

    public ScreenCaptureWindow(ScreenCaptureInfo screenCaptureInfo)
    {
        InitializeComponent();
        this._screenCaptureInfo = screenCaptureInfo;
        this.Position= new PixelPoint(screenCaptureInfo.X, screenCaptureInfo.Y);
        WindowState = WindowState.FullScreen;
        SystemDecorations = SystemDecorations.None;
        Background = new SolidColorBrush(Colors.Black);
        ShowInTaskbar = false;
        if (!Debugger.IsAttached)
        {
            Topmost = true;
        }
        
        
        CanResize = false;
        IsVisible = true;
        WeakReferenceMessenger.Default.Register<string, string>(this, "ScreenCapture", (sender, message) =>
        {
            switch (message)
            {
                case "Close":
                {
                    if (Image is not null && Image.Source is Bitmap bitmap)
                    {
                        bitmap.Dispose();
                        Image.Source = null;
                    }

                    if (MosaicImage is not null && MosaicImage.Source is Bitmap bitmap1)
                    {
                        bitmap1.Dispose();
                        MosaicImage.Source = null;
                    }


                    this.Close();
                    WeakReferenceMessenger.Default.Unregister<string>(this);
                    break;
                }
                case "Selected":
                {
                    IsSelected = true;
                    Cursor?.Dispose();
                    Cursor = Cursor.Default;


                    break;
                }
            }
        });
    }

    public void SetToSelectMode(Action<ScreenCaptureInfo> selectModeAction)
    {
        selectMode = true;
        this.selectModeAction = selectModeAction;
    }

    bool ShowAlignLine => !IsSelected && PointerOver && !Selecting;


    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        ColorPicker.PaletteColors = new[]
        {
            Colors.Red,
            Colors.Yellow,
            Colors.Purple,
            Colors.Orange,
            Colors.Gray,
            Colors.Black,
            Colors.White,
            Colors.Pink,
            Colors.Cyan,
            Colors.Lime,
            Colors.Violet,
            Colors.Aqua,
            Colors.Gold,
            Colors.Chartreuse,
            Colors.Chocolate,
            Colors.Coral,
            Colors.CornflowerBlue,
            Colors.DeepSkyBlue,
            Colors.Fuchsia,
            Colors.Goldenrod,
            Colors.GreenYellow,
            Colors.HotPink,
            Colors.LawnGreen,
        };
        ColorPicker.Color = Colors.Red;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        SelectBox.LocationOrSizeChanged += LocationOrSizeChanged;

        
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        SelectBox.LocationOrSizeChanged -= LocationOrSizeChanged;
        
        renderTargetBitmap?.Dispose();
        MosaicImage.OpacityMask = null;
    }


    protected void LocationOrSizeChanged(object? sender, LocationOrSizeChangedEventArgs locationOrSizeChangedEventArgs)
    {
        UpdateSelectBox();
        UpdateToolBar();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            WeakReferenceMessenger.Default.Send<string, string>("Close", "ScreenCapture");
        }

        if (e.Key == Key.B)
        {
            WindowState = WindowState.Maximized;
        }

        if (e.Key == Key.C)
        {
            WindowState = WindowState.Normal;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        CompletedSelection();
    }

    private void CompletedSelection()
    {
        if (Selecting)
        {
            Selecting = false;
            if (SelectBox.Height < 10)
            {
                SelectBox.Height = 10;
            }

            if (SelectBox.Width < 10)
            {
                SelectBox.Width = 10;
            }

            SelectBox.IsVisible = true;
            IsSelected = true;
            if (!Cursor.ToString()
                    .Equals("Default"))
            {
                Cursor?.Dispose();
                Cursor = Cursor.Default;
            }

            WeakReferenceMessenger.Default.Send<string, string>("Selected", "ScreenCapture");
            UpdateSelectBox();
            if (selectMode)
            {
                selectModeAction?.Invoke(new ScreenCaptureInfo()
                {
                    
                    X = (int)SelectBox._dragTransform.X,
                    Y = (int)SelectBox._dragTransform.Y,
                    Width = (int)SelectBox.Width,
                    Height = (int)SelectBox.Height
                });
                Image.Source = null;

                WeakReferenceMessenger.Default.Send<string, string>("Close", "ScreenCapture");
                this.Close();
            }

            if (ConfigManger.Config.截图直接复制到剪贴板)
            {
                FinnishCapture();
            }
            else
            {
                UpdateToolBar();
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.GetCurrentPoint(this)
                .Properties.IsLeftButtonPressed && !IsSelected)
        {
            Selecting = true;
            X.IsVisible = false;
            Y.IsVisible = false;
            SelectBox.IsVisible = true;
            Cursor?.Dispose();
            Cursor = new Cursor(StandardCursorType.BottomRightCorner);
            _startPoint = e.GetPosition(this);
            e.Pointer.Capture(this);
            //endPoint = e.GetPosition(this);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Right)
        {
            if (!IsSelected)
            {
                WeakReferenceMessenger.Default.Send<string, string>("Close", "ScreenCapture");
            }
        }

        if (Selecting)
        {
            CompletedSelection();
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        PointerOver = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        PointerOver = false;
        X.IsVisible = false;
        Y.IsVisible = false;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.GetCurrentPoint(this)
                .Properties.IsLeftButtonPressed && Selecting)
        {
            var selectBoxHeight = e.GetPosition(this)
                .Y - _startPoint.Y;
            var selectBoxWidth = e.GetPosition(this)
                .X - _startPoint.X;
            if (selectBoxHeight < 0)
            {
                SelectBox.Height = -selectBoxHeight;
                SelectBox._dragTransform.Y = _startPoint.Y + selectBoxHeight;
            }
            else
            {
                SelectBox.Height = selectBoxHeight;
                SelectBox._dragTransform.Y = _startPoint.Y;
            }

            if (selectBoxWidth < 0)
            {
                SelectBox.Width = -selectBoxWidth;
                SelectBox._dragTransform.X = _startPoint.X + selectBoxWidth;
            }
            else
            {
                SelectBox.Width = selectBoxWidth;
                SelectBox._dragTransform.X = _startPoint.X;
            }

            UpdateSelectBox();
        }

        if (ShowAlignLine)
        {
            X.IsVisible = true;
            Y.IsVisible = true;
            X.StartPoint = new Point(0, e.GetPosition(this)
                .Y);

            X.EndPoint = new Point(this.Width, e.GetPosition(this)
                .Y);

            Y.StartPoint = new Point(e.GetPosition(this)
                .X, 0);
            Y.EndPoint = new Point(e.GetPosition(this)
                .X, this.Height);
        }
        else
        {
            X.IsVisible = false;
            Y.IsVisible = false;
        }
    }

    public 截图工具 NowTool = 截图工具.无;
    private bool Adding截图工具 = false;
    private CaptureToolBase Now截图工具;


    private void SelectBox_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        foreach (var canvasChild in Canvas.Children)
        {
            if (canvasChild is CaptureToolBase draggableResizeableControl)
            {
                draggableResizeableControl.IsSelected = false;
            }
        }

        SelectBox.IsSelected = true;
        if (e.GetCurrentPoint(this)
            .Properties.IsLeftButtonPressed)
        {
            if (!(StrokeWidth.Value > 1))
            {
                StrokeWidth.Value = 1;
            }

            switch (NowTool)
            {
                case 截图工具.无:
                {
                    return;
                }
                case 截图工具.矩形:
                {
                    var position = e.GetPosition(this);
                    _startPoint = position;
                    var dragarea = new DraggableResizeableControl();
                    dragarea._dragTransform.X = position.X;
                    dragarea._dragTransform.Y = position.Y;
                    dragarea.IsSelected = true;
                    dragarea.Width = 5;
                    dragarea.Height = 5;
                    var rectangle = new Avalonia.Controls.Shapes.Rectangle();
                    dragarea.Content = rectangle;

                    rectangle.Stroke = new SolidColorBrush(ColorPicker.Color!);
                    rectangle.StrokeThickness = (double)StrokeWidth.Value;

                    Canvas.Children.Add(dragarea);
                    Adding截图工具 = true;
                    Now截图工具 = dragarea;

                    dragarea.Focus();
                    break;
                }
                case 截图工具.圆形:
                {
                    var position = e.GetPosition(this);
                    _startPoint = position;
                    var dragarea = new DraggableResizeableControl();
                    dragarea._dragTransform.X = position.X;
                    dragarea._dragTransform.Y = position.Y;
                    dragarea.Width = 5;
                    dragarea.Height = 5;
                    var rectangle = new Ellipse();
                    dragarea.Content = rectangle;
                    dragarea.IsSelected = true;
                    rectangle.Stroke = new SolidColorBrush(ColorPicker.Color!);
                    rectangle.StrokeThickness = (double)StrokeWidth.Value;

                    Canvas.Children.Add(dragarea);
                    Adding截图工具 = true;
                    Now截图工具 = dragarea;
                    dragarea.Focus();
                    break;
                }
                case 截图工具.箭头:
                {
                    var position = e.GetPosition(this);
                    _startPoint = position;
                    var dragarea = new DraggableArrowControl();
                    dragarea.IsSelected = true;
                    dragarea.Source = position;
                    dragarea.Target = position;
                    dragarea.Stroke = new SolidColorBrush(ColorPicker.Color!);
                    dragarea.Fill = new SolidColorBrush(ColorPicker.Color!);
                    dragarea.StrokeThickness = (double)StrokeWidth.Value;
                    dragarea.ArrowSize = new Size(8 * dragarea.StrokeThickness, 8 * dragarea.StrokeThickness);
                    Canvas.Children.Add(dragarea);
                    Adding截图工具 = true;
                    Now截图工具 = dragarea;
                    dragarea.Focus();
                    break;
                }
                case 截图工具.批准:
                {
                    var position = e.GetPosition(this);
                    _startPoint = position;

                    var rectangle = new PenCaptureTool();
                    rectangle.Points.Add(position);
                    rectangle.StrokeThickness = (double)StrokeWidth.Value;
                    rectangle.Stroke = new SolidColorBrush(ColorPicker.Color!);
                    rectangle.Fill = new SolidColorBrush(ColorPicker.Color!);
                    rectangle.Width = Width;
                    rectangle.Height = Height;
                    Canvas.Children.Add(rectangle);
                    Adding截图工具 = true;
                    Now截图工具 = rectangle;
                    rectangle.Focus();
                    break;
                }
                case 截图工具.文本:
                {
                    var position = e.GetPosition(this);
                    _startPoint = position;
                    var dragarea = new TextCaptureTool();
                    dragarea.IsRedoing = true;
                    dragarea._dragTransform.X = position.X;
                    dragarea._dragTransform.Y = position.Y;


                    dragarea.IsSelected = true;
                    dragarea.Foreground = new SolidColorBrush(ColorPicker.Color!);
                    dragarea.Text = "文本1";
                    dragarea.FontSize = (double)(13 + StrokeWidth.Value);
                    Canvas.Children.Add(dragarea);
                    Adding截图工具 = true;
                    Now截图工具 = dragarea;
                    dragarea.Focus();
                    break;
                }
                case 截图工具.马赛克:
                {
                    var position = e.GetPosition(this);
                    if (MosaicImage.Source is null)
                    {
                       Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (Image.Source is WriteableBitmap writeableBitmap)
                            {
                                unsafe
                                {
                                    var source = new PixelRect(0, 0, (int)writeableBitmap.Size.Width, (int)writeableBitmap.Size.Height);
                                    var bytes = new byte[source.Width*source.Height*4];
                                    fixed (byte* p = bytes)
                                    {
                                        writeableBitmap.CopyPixels( source, (IntPtr)p, source.Width*source.Height*4,  ((((source.Width * writeableBitmap.Format.Value.BitsPerPixel) + 31) & ~31) >> 3)  );
                                    }
                                    var process = GaussianBlur1.GaussianBlur(bytes, source.Width,
                                        source.Height, 4);
                                    var writeableBitmap2 = new WriteableBitmap(
                                        new PixelSize(source.Width, source.Height),
                                        new Vector(96, 96), PixelFormat.Rgba8888 );
                                    using (var l = writeableBitmap2.Lock())
                                    {
                                        for (var r = 0; r < source.Height; r++)
                                        {
                                            Marshal.Copy(process, r *source.Width * 4,
                                                new IntPtr(l.Address.ToInt64() + r * l.RowBytes),
                                                source.Width * 4);
                                        }
                                    }

                                    MosaicImage.Source = writeableBitmap2;
                                }
                            }
                            renderTargetBitmap = new RenderTargetBitmap(new PixelSize((int)Width, (int)Height), new Vector(96, 96));

                            var brush = new ImageBrush(renderTargetBitmap);
                            MosaicImage.OpacityMask = brush;
                            renderTargetBitmap?.Render(MosaicCanvas);
                        });
                    }
                    _startPoint = position;
                    redoStack.Push(new ScreenCaptureRedoInfo()
                    {
                        EditType = ScreenCaptureEditType.移动,
                        Type = 截图工具.马赛克,
                        points = new List<Point>() { position }
                    });
                    MosaicCanvas.Points.Add(position);
                    MosaicCanvas.StrokeThickness = (double)(5 + StrokeWidth.Value);
                    Adding截图工具 = true;

                    break;
                }
            }

            e.Handled = true;
        }
    }

    int count = 0;
    private RenderTargetBitmap? renderTargetBitmap;

    private void SelectBox_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (IsSelected)
        {
            if (!(StrokeWidth.Value > 1))
            {
                StrokeWidth.Value = 1;
            }

            switch (NowTool)
            {
                case 截图工具.无:
                {
                    if (!SelectBox.Cursor.ToString()
                            .Equals("SizeAll"))
                    {
                        SelectBox.Cursor?.Dispose();
                        SelectBox.Cursor = new Cursor(StandardCursorType.SizeAll);
                    }

                    break;
                }
                case 截图工具.马赛克:
                {
                    if (!SelectBox.Cursor.ToString()
                            .Equals("BitmapCursor"))
                    {
                        var round = (int)Math.Round((decimal)StrokeWidth.Value, MidpointRounding.AwayFromZero) + 7;
                        var renderTargetBitmap = new RenderTargetBitmap(new PixelSize(round, round));
                        Ellipse ellipse = new Ellipse();
                        ellipse.Stroke = new SolidColorBrush(ColorPicker.Color!);
                        ellipse.StrokeThickness = 2;
                        ellipse.Width = round;
                        ellipse.Height = round;
                        ellipse.Measure(new Size(round, round));
                        ellipse.Arrange(new Rect(new Point(0, 0), new Size(round, round)));
                        renderTargetBitmap.Render(ellipse);
                        SelectBox.Cursor.Dispose();
                        SelectBox.Cursor = new Cursor(renderTargetBitmap,
                            new PixelPoint(round / 2, round / 2));
                    }

                    break;
                }
                case 截图工具.批准:
                {
                    if (!SelectBox.Cursor.ToString()
                            .Equals("BitmapCursor"))
                    {
                        var round = (int)Math.Round((decimal)StrokeWidth.Value, MidpointRounding.AwayFromZero) + 2;
                        var renderTargetBitmap = new RenderTargetBitmap(new PixelSize(round, round));
                        Ellipse ellipse = new Ellipse();
                        ellipse.Stroke = new SolidColorBrush(ColorPicker.Color!);
                        ellipse.StrokeThickness = 2;
                        ellipse.Width = round;
                        ellipse.Height = round;
                        ellipse.Measure(new Size(round, round));
                        ellipse.Arrange(new Rect(new Point(0, 0), new Size(round, round)));
                        renderTargetBitmap.Render(ellipse);
                        SelectBox.Cursor.Dispose();
                        SelectBox.Cursor = new Cursor(renderTargetBitmap,
                            new PixelPoint(round / 2, round / 2));
                    }

                    break;
                }
                default:
                {
                    if (!SelectBox.Cursor.ToString()
                            .Equals("SizeAll"))
                    {
                        SelectBox.Cursor?.Dispose();
                        SelectBox.Cursor = Avalonia.Input.Cursor.Default;
                    }

                    break;
                }
            }
        }

        if (!Adding截图工具)
        {
            return;
        }

        if (NowTool == 截图工具.文本)
        {
            return;
        }

        if (NowTool == 截图工具.箭头)
        {
            ((DraggableArrowControl)Now截图工具).Target = e.GetPosition(this);
        }
        else if (NowTool == 截图工具.批准)
        {
            ((PenCaptureTool)Now截图工具).Points.Add(e.GetPosition(this));
        }
        else if (NowTool == 截图工具.马赛克)
        {
            if (redoStack.TryPeek(out var result))
            {
                if (result.Type != 截图工具.马赛克)
                {
                    redoStack.Push(new ScreenCaptureRedoInfo()
                    {
                        EditType = ScreenCaptureEditType.移动,
                        Type = 截图工具.马赛克,
                        points = new List<Point>() { e.GetPosition(this) }
                    });
                }
                else
                {
                    redoStack.Peek()
                        .points.Add(e.GetPosition(this));
                }
            }
            else
            {
                redoStack.Push(new ScreenCaptureRedoInfo()
                {
                    EditType = ScreenCaptureEditType.移动,
                    Type = 截图工具.马赛克,
                    points = new List<Point>() { e.GetPosition(this) }
                });
            }


            MosaicCanvas.Points.Add(e.GetPosition(this));
            renderTargetBitmap?.Render(MosaicCanvas);
        }
        else
        {
            var selectBoxHeight = e.GetPosition(this)
                .Y - _startPoint.Y;
            var selectBoxWidth = e.GetPosition(this)
                .X - _startPoint.X;

            if (selectBoxHeight < 0)
            {
                Now截图工具.Height = -selectBoxHeight;
                ((DraggableResizeableControl)Now截图工具)._dragTransform.Y = _startPoint.Y + selectBoxHeight;
            }
            else
            {
                Now截图工具.Height = selectBoxHeight;
                ((DraggableResizeableControl)Now截图工具)._dragTransform.Y = _startPoint.Y;
            }


            if (selectBoxWidth < 0)
            {
                Now截图工具.Width = -selectBoxWidth;
                ((DraggableResizeableControl)Now截图工具)._dragTransform.X = _startPoint.X + selectBoxWidth;
            }
            else
            {
                Now截图工具.Width = selectBoxWidth;
                ((DraggableResizeableControl)Now截图工具)._dragTransform.X = _startPoint.X;
            }
        }

        e.Handled = true;
    }

    private void SelectBox_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && Adding截图工具)
        {
            if (NowTool != 截图工具.马赛克)
            {
                redoStack.Push(new ScreenCaptureRedoInfo
                {
                    Type = NowTool,
                    Target = Now截图工具,
                    EditType = ScreenCaptureEditType.添加,
                    startPoint = _startPoint,
                    Size = Now截图工具.DesiredSize,
                    points = null
                });
            }

            if (NowTool == 截图工具.马赛克)
            {
                if (redoStack.TryPeek(out var result))
                {
                    if (result.Type != 截图工具.马赛克)
                    {
                        redoStack.Push(new ScreenCaptureRedoInfo()
                        {
                            EditType = ScreenCaptureEditType.移动,
                            Type = 截图工具.马赛克,
                            points = new List<Point>() { e.GetPosition(this) }
                        });
                    }
                }
                else
                {
                    redoStack.Push(new ScreenCaptureRedoInfo()
                    {
                        EditType = ScreenCaptureEditType.移动,
                        Type = 截图工具.马赛克,
                        points = new List<Point>() { e.GetPosition(this) }
                    });
                }


                MosaicCanvas.Points.Add(new Point(-1, -1));

                renderTargetBitmap?.Render(MosaicCanvas);
            }

            Adding截图工具 = false;
            e.Handled = true;
        }
    }

    private void SelectBox_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (Adding截图工具)
        {
            if (NowTool != 截图工具.马赛克)
            {
                redoStack.Push(new ScreenCaptureRedoInfo
                {
                    Type = NowTool,
                    Target = Now截图工具,
                    EditType = ScreenCaptureEditType.添加,
                    startPoint = _startPoint,
                    Size = Now截图工具.DesiredSize,
                    points = null
                });
            }

            if (NowTool == 截图工具.马赛克)
            {
                if (redoStack.TryPeek(out var result))
                {
                    if (result.Type == 截图工具.马赛克)
                    {
                        redoStack.Peek()
                            .points.Add(new Point(-1, -1));
                    }
                }


                MosaicCanvas.Points.Add(new Point(-1, -1));
                renderTargetBitmap?.Render(MosaicCanvas);
            }

            Adding截图工具 = false;
            e.Handled = true;
        }
    }


    private void UpdateSelectBox()
    {
        var fullScreenRect = new RectangleGeometry
        {
            Rect = new Rect(0, 0, this.Bounds.Width, this.Bounds.Height),
        };
        var selectionRect = new RectangleGeometry
        {
            Rect = new Rect(new Point(SelectBox._dragTransform.X, SelectBox._dragTransform.Y), SelectBox.DesiredSize),
        };


        var combinedGeometry = new CombinedGeometry
        {
            Geometry1 = fullScreenRect,
            Geometry2 = selectionRect,
            GeometryCombineMode = GeometryCombineMode.Exclude
        };
        Rectangle.Clip = combinedGeometry;
    }

    private void UpdateToolBar()
    {
        ToolBar.IsVisible = true;
        ToolBar.Measure(Bounds.Size);
        if (SelectBox._dragTransform.X + SelectBox.Width + ToolBar.DesiredSize.Width > Bounds.Width)
        {
            ToolBar.SetValue(Canvas.LeftProperty, Bounds.Width - ToolBar.DesiredSize.Width);
        }
        else
        {
            ToolBar.SetValue(Canvas.LeftProperty, SelectBox._dragTransform.X + SelectBox.Width);
        }

        if (SelectBox._dragTransform.Y + SelectBox.Height + ToolBar.DesiredSize.Height > Bounds.Height)
        {
            ToolBar.SetValue(Canvas.TopProperty, Bounds.Height - ToolBar.DesiredSize.Height);
        }
        else
        {
            ToolBar.SetValue(Canvas.TopProperty, SelectBox._dragTransform.Y + SelectBox.Height);
        }
    }

    private void Rectangle_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (IsSelected)
        {
            if (!Cursor.ToString()
                    .Equals("Default"))
            {
                Cursor?.Dispose();
                Cursor = Cursor.Default;
            }
        }
    }


    private void SaveToClipboard_Click(object? sender, RoutedEventArgs e)
    {
        FinnishCapture();
        this.Close();
    }

    private void FinnishCapture()
    {
        if (Image.Source is Bitmap bitmap)
        {
            foreach (var canvasChild in Canvas.Children)
            {
                if (canvasChild is CaptureToolBase draggableResizeableControl)
                {
                    draggableResizeableControl.IsSelected = false;
                }
            }

            SelectBox.IsSelected = false;
            ToolBar.IsVisible = false;
            var renderTargetBitmap =
                new RenderTargetBitmap(new PixelSize(bitmap.PixelSize.Width, bitmap.PixelSize.Height),
                    new Vector(96, 96));

            renderTargetBitmap.Render(this);

            var boundsHeight = (int)(bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4);
            IntPtr ptr = Marshal.AllocHGlobal(boundsHeight);

            renderTargetBitmap.CopyPixels(new PixelRect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height),
                ptr,
                boundsHeight,
                ((((((int)bitmap.PixelSize.Width) * PixelFormat.Rgba8888.BitsPerPixel) + 31) & ~31) >> 3)
            );
            
            byte[] ys = new byte[boundsHeight];
            Marshal.Copy(ptr, ys, 0, boundsHeight);
            Marshal.FreeHGlobal(ptr);
            var image = SixLabors.ImageSharp.Image.LoadPixelData<Bgra32>(ys, bitmap.PixelSize.Width,
                bitmap.PixelSize.Height);
            //image.SaveAsPng("1.png");
            int cropW = 0;
            if ((SelectBox.Width + SelectBox._dragTransform.X)>image.Width)
            {
                cropW = image.Width;
            }else if (SelectBox._dragTransform.X > 0)
            {
                cropW = (int)SelectBox.Width;
            }
            else cropW = (int)SelectBox.Width + (int)SelectBox._dragTransform.X;
            int cropH = 0;
            if ((SelectBox.Height + SelectBox._dragTransform.Y)>image.Height)
            {
                cropH = image.Height;
            }else if (SelectBox._dragTransform.Y > 0)
            {
                cropH = (int)SelectBox.Height;
            }
            else cropH = (int)SelectBox.Height + (int)SelectBox._dragTransform.Y;
            var clone = image.Clone(e => e.Crop(new Rectangle(
                Math.Max((int)SelectBox._dragTransform.X,0), Math.Max((int)SelectBox._dragTransform.Y,0),
                
                cropW, cropH)));
            image.Dispose();
            ServiceManager.Services.GetService<IClipboardService>()
                .SetImageAsync(clone)
                .ContinueWith((e) => clone.Dispose());

            bitmap.Dispose();
            renderTargetBitmap.Dispose();
        }

        Image.Source = null;

        WeakReferenceMessenger.Default.Send<string, string>("Close", "ScreenCapture");
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        if (Image.Source is Bitmap bitmap)
        {
            bitmap.Dispose();
        }

        Image.Source = null;

        WeakReferenceMessenger.Default.Send<string, string>("Close", "ScreenCapture");
        this.Close();
    }

    private Button lastTool;

    private void RectangleButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (lastTool is not null)
        {
            lastTool.Classes.Remove("Selected");
        }

        if (NowTool != 截图工具.矩形)
        {
            NowTool = 截图工具.矩形;
            if (sender is not null)
            {
                lastTool = sender as Button;
                lastTool.Classes.Add("Selected");
            }
        }
        else
            NowTool = 截图工具.无;
    }


    private void CircleButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (lastTool is not null)
        {
            lastTool.Classes.Remove("Selected");
        }

        if (NowTool != 截图工具.圆形)
        {
            NowTool = 截图工具.圆形;
            if (sender is not null)
            {
                lastTool = sender as Button;
                lastTool.Classes.Add("Selected");
            }
        }
        else
            NowTool = 截图工具.无;
    }

    private void ArrowButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (lastTool is not null)
        {
            lastTool.Classes.Remove("Selected");
        }

        if (NowTool != 截图工具.箭头)
        {
            NowTool = 截图工具.箭头;
            if (sender is not null)
            {
                lastTool = sender as Button;
                lastTool.Classes.Add("Selected");
            }
        }
        else
            NowTool = 截图工具.无;
    }

    private void TextButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (lastTool is not null)
        {
            lastTool.Classes.Remove("Selected");
        }

        if (NowTool != 截图工具.文本)
        {
            NowTool = 截图工具.文本;
            if (sender is not null)
            {
                lastTool = sender as Button;
                lastTool.Classes.Add("Selected");
            }
        }
        else
            NowTool = 截图工具.无;
    }

    private void CommentButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (lastTool is not null)
        {
            lastTool.Classes.Remove("Selected");
        }

        if (NowTool != 截图工具.批准)
        {
            NowTool = 截图工具.批准;
            if (sender is not null)
            {
                lastTool = sender as Button;
                lastTool.Classes.Add("Selected");
            }
        }
        else
            NowTool = 截图工具.无;
    }

    private void MosaicButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (lastTool is not null)
        {
            lastTool.Classes.Remove("Selected");
        }

        if (NowTool != 截图工具.马赛克)
        {
            NowTool = 截图工具.马赛克;
            if (sender is not null)
            {
                lastTool = sender as Button;
                lastTool.Classes.Add("Selected");
            }
        }
        else
            NowTool = 截图工具.无;
    }


    private void RedoButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (redoStack.TryPop(out var item))
        {
            switch (item.EditType)
            {
                case ScreenCaptureEditType.添加:
                {
                    Canvas.Children.Remove((Control)item.Target);
                    break;
                }
                case ScreenCaptureEditType.移动:
                {
                    switch (item.Type)
                    {
                        case 截图工具.矩形:
                        {
                            if (Equals(item.Target, SelectBox))
                            {
                                SelectBox._dragTransform.X = item.startPoint.X;
                                SelectBox._dragTransform.Y = item.startPoint.Y;
                                UpdateSelectBox();
                                UpdateToolBar();
                            }
                            else
                            {
                                ((DraggableResizeableControl)Canvas.Children[
                                        Canvas.Children.IndexOf((Control)item.Target)])
                                    ._dragTransform.X = item.startPoint.X;
                                ((DraggableResizeableControl)Canvas.Children[
                                        Canvas.Children.IndexOf((Control)item.Target)])
                                    ._dragTransform.Y = item.startPoint.Y;
                            }

                            break;
                        }
                        case 截图工具.箭头:
                        {
                            ((DraggableArrowControl)Canvas.Children[Canvas.Children.IndexOf((Control)item.Target)])
                                .Source = item.Point1;
                            ((DraggableArrowControl)Canvas.Children[Canvas.Children.IndexOf((Control)item.Target)])
                                .Target = item.Point2;

                            break;
                        }

                        case 截图工具.马赛克:
                        {
                            foreach (var resultPoint in item.points)
                            {
                                MosaicCanvas.Points.Remove(resultPoint);
                            }

                            item.points.Clear();
                            item.points = null;
                            renderTargetBitmap?.Render(MosaicCanvas);
                            break;
                        }
                    }

                    break;
                }
                case ScreenCaptureEditType.调整大小:
                {
                    switch (item.Type)
                    {
                        case 截图工具.矩形:
                        {
                            if (Equals(item.Target, SelectBox))
                            {
                                SelectBox._dragTransform.X = item.startPoint.X;
                                SelectBox._dragTransform.Y = item.startPoint.Y;
                                SelectBox.Width = item.Size.Width;
                                SelectBox.Height = item.Size.Height;
                                UpdateSelectBox();
                                UpdateToolBar();
                            }
                            else
                            {
                                ((DraggableResizeableControl)Canvas.Children[
                                        Canvas.Children.IndexOf((Control)item.Target)])
                                    ._dragTransform.X = item.startPoint.X;
                                ((DraggableResizeableControl)Canvas.Children[
                                        Canvas.Children.IndexOf((Control)item.Target)])
                                    ._dragTransform.Y = item.startPoint.Y;
                                ((DraggableResizeableControl)Canvas.Children[
                                        Canvas.Children.IndexOf((Control)item.Target)])
                                    .Width = item.Size.Width;
                                ((DraggableResizeableControl)Canvas.Children[
                                        Canvas.Children.IndexOf((Control)item.Target)])
                                    .Height = item.Size.Height;
                            }

                            break;
                        }
                        case 截图工具.文本:
                        {
                            ((TextCaptureTool)Canvas.Children[Canvas.Children.IndexOf((Control)item.Target)])
                                .IsRedoing = true;
                            ((TextCaptureTool)Canvas.Children[Canvas.Children.IndexOf((Control)item.Target)])
                                .Text = (string)item.Data;
                            break;
                        }
                    }

                    break;
                }
                case ScreenCaptureEditType.画笔:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}