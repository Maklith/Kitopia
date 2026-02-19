using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Core.Services.Config;
using Core.Utils;
using Core.Utils.ImageTools;
using KitopiaAvalonia.Controls.Capture;
using KitopiaAvalonia.SDKs;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using PluginCore;
using PluginCore.ExMethod;
using SharpHook;
using SharpHook.Native;
using Ursa.Controls;
using Math = System.Math;
using MouseButton = Avalonia.Input.MouseButton;
using Point = Avalonia.Point;
using Rect = Avalonia.Rect;
using Size = Avalonia.Size;
using Window = Avalonia.Controls.Window;

namespace KitopiaAvalonia.Windows;

public partial class ScreenCaptureWindow : Window
{
    private WindowInfo _currentWindowInfo;
    private Point _pointerStartPoint;
    private ScreenCaptureInfo _screenCaptureInfo;
    private Point _startPoint;
    private List<WindowInfo> _windowInfos;
    private List<ScreenCaptureInfo> _screens = new();
    private bool Adding截图工具 = false;

    private int count = 0;
    private bool Finish = false;

    private Button lastTool;
    private CaptureToolBase Now截图工具;

    private SelectionState NowSelectionState = SelectionState.None;

    public 截图工具 NowTool = 截图工具.无;
    public Stack<ScreenCaptureRedoInfo> redoStack = new();
    private RenderTargetBitmap? renderTargetBitmap;
    private bool selectBytesMode = false;
    private Action<ScreenCaptureResult> selectBytesModeAction;
    private Action selectBytesModeCancelAction;
    private bool selectMode = false;
    private Action<ScreenCaptureInfo> selectModeAction;
    private List<CaptureToolBase> tools = new();
    
    private byte[]? _screenPixels;
    private int _pixelWidth;
    private int _pixelHeight;
    private WriteableBitmap? _magnifierBmp;
    private const int MagnifierSize = 11; // 11x11 grid

    public ScreenCaptureWindow(IEnumerable<ScreenCaptureResult> screenCaptureResults)
    {
        InitializeComponent();
        _magnifierBmp = new WriteableBitmap(new PixelSize(MagnifierSize, MagnifierSize), new Vector(96, 96), PixelFormat.Bgra8888);
        MagnifierImage.Source = _magnifierBmp;

        _windowInfos = ServiceManager.Services.GetService<IScreenCaptureManager>()!.GetAllWindowInfo();
        
        var results = screenCaptureResults.ToList();
        _screens = results.Select(r => r.Info).ToList();
        
        int minX = 0, minY = 0, width = 0, height = 0;
        
        if (results.Count > 0) 
        {
            minX = results.Min(r => r.Info.ScreenInfo.X);
            minY = results.Min(r => r.Info.ScreenInfo.Y);
            int maxX = results.Max(r => r.Info.ScreenInfo.X + r.Info.ScreenInfo.Width);
            int maxY = results.Max(r => r.Info.ScreenInfo.Y + r.Info.ScreenInfo.Height);
            width = maxX - minX;
            height = maxY - minY;
        }

        if (width > 0 && height > 0) 
        {
            // Combined Mat
            using var combinedMat = new Mat(height, width, MatType.CV_8UC4, Scalar.All(0));
            
            foreach (var result in results)
            {
                if (result.Source != null && !result.Source.IsDisposed)
                {
                    int x = result.Info.ScreenInfo.X - minX;
                    int y = result.Info.ScreenInfo.Y - minY;
                    
                    if (x >= 0 && y >= 0 && x + result.Source.Width <= width && y + result.Source.Height <= height)
                    {
                        using var roiMat = combinedMat[new OpenCvSharp.Rect(x, y, result.Source.Width, result.Source.Height)];
                        result.Source.CopyTo(roiMat);
                    }
                }
            }
            
            Image.Source = combinedMat.ToAWriteableBitmap();

            if (combinedMat.Total() > 0)
            {
                _pixelWidth = combinedMat.Width;
                _pixelHeight = combinedMat.Height;
                // CV_8UC4 is 4 bytes per pixel
                var length = _pixelWidth * _pixelHeight * 4;
                _screenPixels = new byte[length];
                unsafe
                {
                    Marshal.Copy((IntPtr)combinedMat.DataPointer, _screenPixels, 0, length);
                }
            }
            
            _screenCaptureInfo = new ScreenCaptureInfo
            {
                ScreenCaptureType = ScreenCaptureType.屏幕,
                ScreenInfo = new ScreenInfo
                {
                    X = minX,
                    Y = minY,
                    Width = width,
                    Height = height,
                    hMonitor = IntPtr.Zero
                }
            };

            Position = new PixelPoint(minX, minY);
            
            var scaling = 1.0;
            var screen = Screens.ScreenFromPoint(new PixelPoint(minX, minY));
            if (screen != null)
            {
                scaling = screen.Scaling;
            }

            Width = width / scaling;
            Height = height / scaling;
        }
        else
        {
            _screenCaptureInfo = new ScreenCaptureInfo();
        }
        
        WindowState = WindowState.Normal;
        SystemDecorations = SystemDecorations.None;
        Background = new SolidColorBrush(Colors.Black);
        ShowInTaskbar = false;

        if (!Debugger.IsAttached) Topmost = true;


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


                    Close();
                    WeakReferenceMessenger.Default.Unregister<string>(this);
                    break;
                }
                case "Selected":
                {
                    NowSelectionState = SelectionState.Selected;
                    Cursor?.Dispose();
                    Cursor = Cursor.Default;


                    break;
                }
            }
        });
    }

    private bool _isLongCapturing = false;
    private bool _isWaitingForLongCaptureClick = false;
    private TaskCompletionSource? _longCaptureClickTaskSource;

    private async void LongCaptureButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_isLongCapturing) return;
        _isLongCapturing = true;

        try
        {
            // 1. Get current selection info
            var captureInfo = GetSelectedScreenCaptureInfo();
            if (captureInfo.Width <= 0 || captureInfo.Height <= 0)
            {
                ServiceManager.Services.GetService<IToastService>()?.Show("提示", "请先选择区域", NotificationType.Warning);
                _isLongCapturing = false;
                return;
            }

            // 2. Wait for user to click the scrollable area
            ToolBar.IsVisible = false;
            LongCaptureTooltip.IsVisible = true;
            _isWaitingForLongCaptureClick = true;
            _longCaptureClickTaskSource = new TaskCompletionSource();
            
            await _longCaptureClickTaskSource.Task;
            
            _isWaitingForLongCaptureClick = false;
            LongCaptureTooltip.IsVisible = false;

            // 3. Hide window to reveal content behind
            this.Hide();
            
            await Task.Delay(500); // Wait for animation/hide

            var captureManager = ServiceManager.Services.GetService<IScreenCaptureManager>();
            var simulator = new EventSimulator();

            // 4. Initial Capture
            var accumulatorResult = captureManager!.CaptureScreenBytes(captureInfo);
            var accumulator = accumulatorResult.Source;
            
            // Progress window
            var progressWindow = new LongScreenshotProgressWindow();
            progressWindow.Width = SelectBox.Bounds.Width;
            progressWindow.Height = SelectBox.Bounds.Height;
            progressWindow.Position = new PixelPoint(captureInfo.X, (int)(captureInfo.Y- captureInfo.Height ));
            progressWindow.Show();
            
            if (accumulator == null || accumulator.Empty())
            {
                 ServiceManager.Services.GetService<IToastService>()?.Show("错误", "初始截图失败", NotificationType.Error);
                 progressWindow.Close();
                 this.Show();
                 _isLongCapturing = false;
                 return;
            }
            
            progressWindow.UpdateImage(accumulator);

            // Setup Global Hook to stop on any key
            using var hook = new SimpleGlobalHook();
            hook.KeyPressed += (_, _) => progressWindow.RequestStop();
            hook.RunAsync();

            // 5. Scroll and Stitch Loop
            int maxScrolls = 50;
            
            double stepRatio = captureInfo.Height / 600.0;
            int stepMagnitude = (int)(120 * stepRatio);
            
            if (stepMagnitude < 120) stepMagnitude = 120;
            if (stepMagnitude > 360) stepMagnitude = 360; 

            short scrollStep = (short)-stepMagnitude; 
            
            for (int i = 0; i < maxScrolls; i++)
            {
                if (progressWindow.IsStopRequested) break;
                
                simulator.SimulateMouseWheel((short)scrollStep);
                await Task.Delay(500); 

                var newResult = captureManager.CaptureScreenBytes(captureInfo);
                var newFrame = newResult.Source;

                if (newFrame == null || newFrame.Empty())
                {
                    break;
                }

                var stitched = ImageStitcher.StitchImages(accumulator, newFrame);
                newFrame.Dispose(); 

                if (stitched != null)
                {
                    var oldAccumulator = accumulator;
                    accumulator = stitched;
                    oldAccumulator.Dispose();
                    progressWindow.UpdateImage(accumulator);
                }
                else
                {
                    break;
                }
            }
            
            progressWindow.Close();

            // 6. Restore Window
            this.Show();
            
            // 7. Finish
            ServiceManager.Services.GetService<IClipboardService>()!
                            .SetImageAsync(new ScreenCaptureResult
                            {
                                Info = captureInfo, 
                                Source = accumulator.Clone() 
                            });
            
             accumulator.Dispose();
             this.Close();
             WeakReferenceMessenger.Default.Send<string, string>("Close", "ScreenCapture");
            
             ServiceManager.Services.GetService<IToastService>().Show("成功", "长截图已复制到剪贴板", NotificationType.Success);

        }
        catch (Exception ex)
        {
             ServiceManager.Services.GetService<IToastService>()?.Show("错误", $"长截图失败: {ex.Message}", NotificationType.Error);
             this.Show();
        }
        finally
        {
            _isLongCapturing = false;
        }
    }

    private bool ShowAlignLine => NowSelectionState == SelectionState.Selected;

    public void SetToSelectMode(Action<ScreenCaptureInfo> selectModeAction)
    {
        selectMode = true;
        this.selectModeAction = selectModeAction;
    }

    public void SetToSelectBytesMode(Action<ScreenCaptureResult> selectBytesModeAction,
        Action selectBytesModeCancelAction)
    {
        selectBytesMode = true;
        this.selectBytesModeAction = selectBytesModeAction;
        this.selectBytesModeCancelAction = selectBytesModeCancelAction;
    }


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
            Colors.LawnGreen
        };
        ColorPicker.Color = Colors.Red;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        SelectBox.LocationOrSizeChanged += LocationOrSizeChanged;
        StrokeWidth.ValueChanged += StrokeWidthOnValueChanged;
        ColorPicker.ColorChanged += ColorPickerOnColorChanged;
    }


    protected override void OnClosed(EventArgs e)
    {
        SelectBox.LocationOrSizeChanged -= LocationOrSizeChanged;
        StrokeWidth.ValueChanged -= StrokeWidthOnValueChanged;
        ColorPicker.ColorChanged -= ColorPickerOnColorChanged;
        renderTargetBitmap?.Dispose();
        MosaicImage.OpacityMask = null;

        if (selectBytesMode && !Finish) selectBytesModeCancelAction.Invoke();

        base.OnClosed(e);
    }

    private void ColorPickerOnColorChanged(object? sender, ColorChangedEventArgs e)
    {
        switch (Now截图工具)
        {
            case DraggableArrowControl draggableArrowControl:
                draggableArrowControl.Stroke = new SolidColorBrush(e.NewColor);
                draggableArrowControl.Fill = new SolidColorBrush(e.NewColor);
                break;
            case DraggableResizeableControl draggableResizeableControl:
                ((Shape)draggableResizeableControl.Content).Stroke = new SolidColorBrush(e.NewColor);
                break;
            case PenCaptureTool penCaptureTool:
                penCaptureTool.Stroke = new SolidColorBrush(e.NewColor);
                break;
            case TextCaptureTool textCaptureTool:
                textCaptureTool.Foreground = new SolidColorBrush(e.NewColor);
                break;
        }
    }

    private void StrokeWidthOnValueChanged(object? sender, ValueChangedEventArgs<int> valueChangedEventArgs)
    {
        switch (Now截图工具)
        {
            case DraggableArrowControl draggableArrowControl:
                draggableArrowControl.StrokeThickness = (double)valueChangedEventArgs.NewValue;
                draggableArrowControl.ArrowSize = new Size(8 * draggableArrowControl.StrokeThickness,
                    8 * draggableArrowControl.StrokeThickness);
                break;
            case DraggableResizeableControl draggableResizeableControl:
                ((Shape)draggableResizeableControl.Content).StrokeThickness = (double)valueChangedEventArgs.NewValue;
                break;
            case PenCaptureTool penCaptureTool:
                penCaptureTool.StrokeThickness = (double)valueChangedEventArgs.NewValue;
                break;
            case TextCaptureTool textCaptureTool:
                textCaptureTool.FontSize = (double)(13 + valueChangedEventArgs.NewValue);
                break;
        }

        if (NowTool == 截图工具.马赛克)
        {
            MosaicCanvas.StrokeThickness = (double)(5 + valueChangedEventArgs.NewValue);
            renderTargetBitmap?.Render(MosaicCanvas);
        }
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
            if (NowSelectionState == SelectionState.Selected)
            {
                if (Redo()) return;
                WeakReferenceMessenger.Default.Send<string, string>("Close", "ScreenCapture");
            }

            WeakReferenceMessenger.Default.Send<string, string>("Close", "ScreenCapture");
        }

        if (e.Key == Key.B) WindowState = WindowState.Maximized;

        if (e.Key == Key.C)
        {
             if (ColorInspector.IsVisible)
             {
                 var hex = ColorHex.Text;
                 if (!string.IsNullOrEmpty(hex))
                 {
                     ServiceManager.Services.GetService<IClipboardService>()?.SetText(hex);
                     ServiceManager.Services.GetService<IToastService>()?.Show("复制成功", $"已复制 HEX: {hex}", NotificationType.Success);
                     e.Handled = true;
                     return;
                 }
             }
             else 
             {
                 WindowState = WindowState.Normal;
             }
        }
        
        if (ColorInspector.IsVisible)
        {
            if (e.Key == Key.R)
            {
                var rgb = ColorRgb.Text?.Replace("RGB: ", "");
                if (!string.IsNullOrEmpty(rgb))
                {
                    ServiceManager.Services.GetService<IClipboardService>()?.SetText(rgb);
                    ServiceManager.Services.GetService<IToastService>()?.Show("复制成功", $"已复制 RGB: {rgb}", NotificationType.Success);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.H)
            {
                var hsv = ColorHsv.Text?.Replace("HSV: ", "");
                if (!string.IsNullOrEmpty(hsv))
                {
                    ServiceManager.Services.GetService<IClipboardService>()?.SetText(hsv);
                    ServiceManager.Services.GetService<IToastService>()?.Show("复制成功", $"已复制 HSV: {hsv}", NotificationType.Success);
                     e.Handled = true;
                }
            }
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        CompletedSelection();
    }

    private void CompletedSelection()
    {
        if (NowSelectionState == SelectionState.Selected) return;
        if (NowSelectionState == SelectionState.MoveSelecting) _startPoint = _pointerStartPoint;
        NowSelectionState = SelectionState.Selected;
        if (SelectBox.Height < 10) SelectBox.Height = 10;

        if (SelectBox.Width < 10) SelectBox.Width = 10;

        SelectBox.IsVisible = true;
        if (Cursor != null && !Cursor.ToString()
                .Equals("Default"))
        {
            Cursor?.Dispose();
            Cursor = Cursor.Default;
        }

        WeakReferenceMessenger.Default.Send<string, string>("Selected", "ScreenCapture");
        UpdateSelectBox();

        if (ConfigManger.Config.截图直接复制到剪贴板 || selectBytesMode || selectMode)
            FinnishCapture();
        else
            UpdateToolBar();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (_isWaitingForLongCaptureClick)
        {
            _longCaptureClickTaskSource?.TrySetResult();
            e.Handled = true;
            return;
        }

        if (NowSelectionState == SelectionState.Selected) return;
        if (e.GetCurrentPoint(this)
            .Properties.IsLeftButtonPressed)
        {
            NowSelectionState = SelectionState.WindowSelecting;
            SelectBox.IsVisible = true;
            Cursor?.Dispose();
            Cursor = new Cursor(StandardCursorType.BottomRightCorner);
            _startPoint = e.GetPosition(this);
            _pointerStartPoint = e.GetPosition(this);
            e.Pointer.Capture(this);
            //endPoint = e.GetPosition(this);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Right)
            if (NowSelectionState == SelectionState.None)
                WeakReferenceMessenger.Default.Send<string, string>("Close", "ScreenCapture");
        if (NowSelectionState == SelectionState.Selected) return;
        CompletedSelection();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (NowSelectionState == SelectionState.Selected) return;
        NowSelectionState = SelectionState.WindowSelecting;
        if (NowSelectionState == SelectionState.WindowSelecting) SelectWindow(e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (NowSelectionState != SelectionState.None) return;

        if (NowSelectionState != SelectionState.Selected) NowSelectionState = SelectionState.None;

        SelectBox.Width = 0;
        SelectBox.Height = 0;
        SelectBox.IsVisible = false;
        UpdateSelectBox();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        
        UpdateColorInspector(e.GetPosition(this));
        
        if (_isWaitingForLongCaptureClick)
        {
            var pos = e.GetPosition(this);
            LongCaptureTooltip.SetValue(Canvas.LeftProperty, pos.X + 15);
            LongCaptureTooltip.SetValue(Canvas.TopProperty, pos.Y + 15);
            return;
        }

        if (NowSelectionState == SelectionState.Selected) return;

        if (NowSelectionState == SelectionState.None) NowSelectionState = SelectionState.WindowSelecting;
        var position = e.GetPosition(this);

        if (e.Properties.IsLeftButtonPressed && NowSelectionState is SelectionState.WindowSelecting
                                             && Math.Pow(position.Y - _startPoint.Y, 2) +
                                             Math.Pow(position.X - _startPoint.X, 2) > 1300)
        {
            NowSelectionState = SelectionState.MoveSelecting;
            _startPoint = _pointerStartPoint;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            if (NowSelectionState == SelectionState.MoveSelecting)
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

                _currentWindowInfo = new WindowInfo();
                UpdateSelectBox();
            }

        if (NowSelectionState == SelectionState.WindowSelecting) SelectWindow(e);
    }

    private void SelectWindow(PointerEventArgs e)
    {
        var currentPoint = e.GetCurrentPoint(this);
        var pixelPoint = this.PointToScreen(currentPoint.Position);
        var positionX = pixelPoint.X;
        var positionY = pixelPoint.Y;

        var firstOrDefault = _windowInfos.Where(e => positionX >= e.Rect.X && positionX <= e.Rect.X + e.Rect.Width &&
                                                     positionY >= e.Rect.Y && positionY <= e.Rect.Y + e.Rect.Height)
            .OrderBy(e => e.ZIndex).ToList();
        
        Rect targetRectPhysical;

        if (firstOrDefault.Count() == 0)
        {
            // Fallback: Check which screen we are on
            var screen = _screens.FirstOrDefault(s => positionX >= s.ScreenInfo.X && positionX < s.ScreenInfo.X + s.ScreenInfo.Width &&
                                                      positionY >= s.ScreenInfo.Y && positionY < s.ScreenInfo.Y + s.ScreenInfo.Height);

            if (!screen.Equals(default(ScreenCaptureInfo)))
            {
                // Found a screen
                targetRectPhysical = new Rect(screen.ScreenInfo.X, screen.ScreenInfo.Y, screen.ScreenInfo.Width, screen.ScreenInfo.Height);
                _currentWindowInfo = new WindowInfo(); // No specific window
            }
            else
            {
                // Fallback to full canvas if no screen matches (unlikely if strictly inside bounds)
                _currentWindowInfo = new WindowInfo();
                _startPoint = new Point(0, 0);
                SelectBox._dragTransform.X = 0;
                SelectBox._dragTransform.Y = 0;
                SelectBox.Width = Bounds.Width;
                SelectBox.Height = Bounds.Height;
                SelectBox.IsVisible = true;
                UpdateSelectBox();
                return;
            }
        }
        else
        {
            var windowInfo = firstOrDefault.FirstOrDefault();
            _currentWindowInfo = windowInfo;
            targetRectPhysical = new Rect(windowInfo.Rect.X, windowInfo.Rect.Y, windowInfo.Rect.Width, windowInfo.Rect.Height);
        }
        
        // Convert Physical Rect back to Logical coordinates for SelectBox
        var topLeft = this.PointToClient(new PixelPoint((int)targetRectPhysical.X, (int)targetRectPhysical.Y));
        var bottomRight = this.PointToClient(new PixelPoint((int)(targetRectPhysical.X + targetRectPhysical.Width), (int)(targetRectPhysical.Y + targetRectPhysical.Height)));

        // Handle negative coordinates or off-canvas mapping if necessary, though PointToClient should handle it relative to window origin
        var width = bottomRight.X - topLeft.X;
        var height = bottomRight.Y - topLeft.Y;
        
        _startPoint = topLeft;
        SelectBox._dragTransform.X = topLeft.X;
        SelectBox._dragTransform.Y = topLeft.Y;
        SelectBox.Width = width;
        SelectBox.Height = height;

        SelectBox.IsVisible = true;
        UpdateSelectBox();
    }


    private void SelectBox_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isWaitingForLongCaptureClick)
        {
            _longCaptureClickTaskSource?.TrySetResult();
            e.Handled = true;
            return;
        }

        foreach (var canvasChild in Canvas.Children)
            if (canvasChild is CaptureToolBase draggableResizeableControl)
                draggableResizeableControl.IsSelected = false;

        SelectBox.IsSelected = true;
        if (e.GetCurrentPoint(this)
            .Properties.IsLeftButtonPressed)
        {
            if (!(StrokeWidth.Value > 1)) StrokeWidth.Value = 1;

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
                    var rectangle = new Rectangle();
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
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (Image.Source is WriteableBitmap writeableBitmap)
                                unsafe
                                {
                                    var source = new PixelRect(0, 0, (int)writeableBitmap.Size.Width,
                                        (int)writeableBitmap.Size.Height);
                                    var bytes = new byte[source.Width * source.Height * 4];
                                    fixed (byte* p = bytes)
                                    {
                                        writeableBitmap.CopyPixels(source, (IntPtr)p, source.Width * source.Height * 4,
                                            ((source.Width * writeableBitmap.Format.Value.BitsPerPixel + 31) & ~31) >>
                                            3);
                                    }

                                    var process = GaussianBlur1.GaussianBlur(bytes, source.Width,
                                        source.Height, 4);
                                    var writeableBitmap2 = new WriteableBitmap(
                                        new PixelSize(source.Width, source.Height),
                                        new Vector(96, 96), PixelFormat.Bgra8888);
                                    using (var l = writeableBitmap2.Lock())
                                    {
                                        for (var r = 0; r < source.Height; r++)
                                            Marshal.Copy(process, r * source.Width * 4,
                                                new IntPtr(l.Address.ToInt64() + r * l.RowBytes),
                                                source.Width * 4);
                                    }

                                    MosaicImage.Source = writeableBitmap2;
                                }

                            renderTargetBitmap = new RenderTargetBitmap(new PixelSize((int)Width, (int)Height),
                                new Vector(96, 96));

                            var brush = new ImageBrush(renderTargetBitmap);
                            MosaicImage.OpacityMask = brush;
                            renderTargetBitmap?.Render(MosaicCanvas);
                        });
                    _startPoint = position;
                    redoStack.Push(new ScreenCaptureRedoInfo
                    {
                        EditType = ScreenCaptureEditType.移动,
                        Type = 截图工具.马赛克,
                        points = new List<Point> { position }
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

    private void SelectBox_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isWaitingForLongCaptureClick)
        {
            var pos = e.GetPosition(this);
            LongCaptureTooltip.SetValue(Canvas.LeftProperty, pos.X + 15);
            LongCaptureTooltip.SetValue(Canvas.TopProperty, pos.Y + 15);
            return;
        }

        if (NowSelectionState == SelectionState.Selected)
        {
            if (!(StrokeWidth.Value > 1)) StrokeWidth.Value = 1;

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
                        var ellipse = new Ellipse();
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
                        var ellipse = new Ellipse();
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
                        SelectBox.Cursor = Cursor.Default;
                    }

                    break;
                }
            }
        }

        if (!Adding截图工具) return;

        if (NowTool == 截图工具.文本) return;

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
                    redoStack.Push(new ScreenCaptureRedoInfo
                    {
                        EditType = ScreenCaptureEditType.移动,
                        Type = 截图工具.马赛克,
                        points = new List<Point> { e.GetPosition(this) }
                    });
                else
                    redoStack.Peek()
                        .points.Add(e.GetPosition(this));
            }
            else
            {
                redoStack.Push(new ScreenCaptureRedoInfo
                {
                    EditType = ScreenCaptureEditType.移动,
                    Type = 截图工具.马赛克,
                    points = new List<Point> { e.GetPosition(this) }
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
                redoStack.Push(new ScreenCaptureRedoInfo
                {
                    Type = NowTool,
                    Target = Now截图工具,
                    EditType = ScreenCaptureEditType.添加,
                    startPoint = _startPoint,
                    Size = Now截图工具.DesiredSize,
                    points = null
                });

            if (NowTool == 截图工具.马赛克)
            {
                if (redoStack.TryPeek(out var result))
                {
                    if (result.Type != 截图工具.马赛克)
                        redoStack.Push(new ScreenCaptureRedoInfo
                        {
                            EditType = ScreenCaptureEditType.移动,
                            Type = 截图工具.马赛克,
                            points = new List<Point> { e.GetPosition(this) }
                        });
                }
                else
                {
                    redoStack.Push(new ScreenCaptureRedoInfo
                    {
                        EditType = ScreenCaptureEditType.移动,
                        Type = 截图工具.马赛克,
                        points = new List<Point> { e.GetPosition(this) }
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
                redoStack.Push(new ScreenCaptureRedoInfo
                {
                    Type = NowTool,
                    Target = Now截图工具,
                    EditType = ScreenCaptureEditType.添加,
                    startPoint = _startPoint,
                    Size = Now截图工具.DesiredSize,
                    points = null
                });

            if (NowTool == 截图工具.马赛克)
            {
                if (redoStack.TryPeek(out var result))
                    if (result.Type == 截图工具.马赛克)
                        redoStack.Peek()
                            .points.Add(new Point(-1, -1));


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
            Rect = new Rect(0, 0, Bounds.Width, Bounds.Height)
        };
        var selectionRect = new RectangleGeometry
        {
            Rect = new Rect(SelectBox._dragTransform.X, SelectBox._dragTransform.Y, SelectBox.Width,
                SelectBox.Height)
        };


        var combinedGeometry = new CombinedGeometry
        {
            Geometry1 = fullScreenRect,
            Geometry2 = selectionRect,
            GeometryCombineMode = GeometryCombineMode.Exclude
        };

        Rectangle.Clip = combinedGeometry;
        Rectangle.InvalidateVisual();
        //Console.WriteLine("SelectBox: " + SelectBox._dragTransform.X + ", " + SelectBox._dragTransform.Y + ", " + SelectBox.Width + ", " + SelectBox.Height);
    }

    private void UpdateToolBar()
    {
        ToolBar.IsVisible = true;
        ToolBar.Measure(Bounds.Size);
        var margin = 5.0;
        var toolBarWidth = ToolBar.DesiredSize.Width;
        var toolBarHeight = ToolBar.DesiredSize.Height;

        // Determine scaling and current screen logical bounds
        var scaling = 1.0;
        var primaryScreen = Screens.ScreenFromPoint(Position);
        if (primaryScreen != null) scaling = primaryScreen.Scaling;

        var selCenterLogical = new Point(SelectBox._dragTransform.X + SelectBox.Width / 2,
                                         SelectBox._dragTransform.Y + SelectBox.Height / 2);
        var selCenterPhysical = Position + PixelPoint.FromPoint(selCenterLogical, scaling);
        var targetScreen = Screens.ScreenFromPoint(selCenterPhysical);

        double minX_logical, minY_logical, maxX_logical, maxY_logical;

        if (targetScreen != null)
        {
            minX_logical = (targetScreen.Bounds.X - Position.X) / scaling;
            minY_logical = (targetScreen.Bounds.Y - Position.Y) / scaling;
            maxX_logical = minX_logical + targetScreen.Bounds.Width / scaling;
            maxY_logical = minY_logical + targetScreen.Bounds.Height / scaling;
        }
        else
        {
            minX_logical = 0;
            minY_logical = 0;
            maxX_logical = Bounds.Width;
            maxY_logical = Bounds.Height;
        }

        var left = SelectBox._dragTransform.X + SelectBox.Width + margin;
        var top = SelectBox._dragTransform.Y + SelectBox.Height + margin;

        if (left + toolBarWidth + margin > maxX_logical)
        {
            left = maxX_logical - toolBarWidth - margin;
        }
        if (left < minX_logical + margin) left = minX_logical + margin;

        if (top + toolBarHeight + margin > maxY_logical)
        {
            var topAbove = SelectBox._dragTransform.Y - toolBarHeight - margin;
            if (topAbove >= minY_logical + margin)
            {
                top = topAbove;
            }
            else
            {
                top = maxY_logical - toolBarHeight - margin;
            }
        }
        if (top < minY_logical + margin) top = minY_logical + margin;

        ToolBar.SetValue(Canvas.LeftProperty, left);
        ToolBar.SetValue(Canvas.TopProperty, top);
    }

    private void Rectangle_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (NowSelectionState == SelectionState.Selected)
            if (!Cursor.ToString()
                    .Equals("Default"))
            {
                Cursor?.Dispose();
                Cursor = Cursor.Default;
            }
    }


    private void UpdateColorInspector(Point p)
    {
        if (_screenPixels == null || ColorInspector == null || _pixelWidth == 0 || _pixelHeight == 0) return;
        
        // Don't show if selecting
        if (NowSelectionState != SelectionState.None && NowSelectionState != SelectionState.WindowSelecting) 
        {
             ColorInspector.IsVisible = false;
             return;
        }

        double scaleX = _pixelWidth / Bounds.Width;
        double scaleY = _pixelHeight / Bounds.Height;
        
        int centerX = (int)(p.X * scaleX);
        int centerY = (int)(p.Y * scaleY);

        if (centerX < 0 || centerX >= _pixelWidth || centerY < 0 || centerY >= _pixelHeight) 
        {
             ColorInspector.IsVisible = false;
             return;
        }
        
        // Update Color Info
        int centerIndex = (centerY * _pixelWidth + centerX) * 4;
        if (centerIndex >= 0 && centerIndex + 3 < _screenPixels.Length) 
        {
            byte b = _screenPixels[centerIndex];
            byte g = _screenPixels[centerIndex + 1];
            byte r = _screenPixels[centerIndex + 2];
            // byte a = _screenPixels[centerIndex + 3]; // Ignore alpha for now, screen capture usually opaque

            Color color = Color.FromRgb(r, g, b);
            ColorPreview.Background = new SolidColorBrush(color);
            ColorHex.Text = $"#{r:X2}{g:X2}{b:X2}";
            ColorRgb.Text = $"RGB: {r}, {g}, {b}";
            
            var hsv = color.ToHsv();
            ColorHsv.Text = $"HSV: {hsv.H:F0}, {hsv.S:F2}, {hsv.V:F2}";
        }

        // Update Magnifier
        if (_magnifierBmp != null)
        {
            using (var buf = _magnifierBmp.Lock())
            {
                unsafe
                {
                    uint* ptr = (uint*)buf.Address;
                    int halfSize = MagnifierSize / 2;
                    
                    for (int y = 0; y < MagnifierSize; y++)
                    {
                        for (int x = 0; x < MagnifierSize; x++)
                        {
                            int sampleX = centerX + (x - halfSize);
                            int sampleY = centerY + (y - halfSize);
                            
                            uint pixelValue = 0xFF000000; // Black default

                            if (sampleX >= 0 && sampleX < _pixelWidth && sampleY >= 0 && sampleY < _pixelHeight)
                            {
                                int srcIdx = (sampleY * _pixelWidth + sampleX) * 4;
                                // Read BGRA
                                byte pb = _screenPixels[srcIdx];
                                byte pg = _screenPixels[srcIdx + 1];
                                byte pr = _screenPixels[srcIdx + 2];
                                byte pa = 255; 
                                
                                // Write BGRA (Little Endian uint: A R G B -> 0xAARRGGBB? No, Skia/WPF use BGRA or ARGB depending on platform, but usually BGRA on Windows)
                                // WriteableBitmap PixelFormat.Bgra8888 matches this.
                                // 0xAARRGGBB in uint is B | G<<8 | R<<16 | A<<24
                                pixelValue = (uint)(pb | (pg << 8) | (pr << 16) | (pa << 24));
                            }
                            
                            ptr[y * MagnifierSize + x] = pixelValue;
                        }
                    }
                }
            }
            // Force redraw? WriteableBitmap usually updates on unlock/invalidate
            // But we need to make sure the Image control sees it? 
            // Lock/Unlock handles it in Avalonia.
             // Triggers update
             //_magnifierBmp.RaiseEvent? No, Lock disposal does it.
             // We might need to invalidate the Image visual if it doesn't update automatically (it usually does).
             MagnifierImage.InvalidateVisual();
        }
        
        // Position popup
        double popupX = p.X + 20;
        double popupY = p.Y + 20;
        
        if (popupX + 130 > Bounds.Width) popupX = p.X - 140;
        if (popupY + 180 > Bounds.Height) popupY = p.Y - 190;

        Canvas.SetLeft(ColorInspector, popupX);
        Canvas.SetTop(ColorInspector, popupY);
        ColorInspector.IsVisible = true;
    }

    private void SaveToClipboard_Click(object? sender, RoutedEventArgs e)
    {
        FinnishCapture();

        Close();
    }

    private ScreenCaptureInfo GetSelectedScreenCaptureInfo()
    {
        if (Image.Source is Bitmap bitmap)
        {
            var cropW = 0;
            var dragTransformX = SelectBox._dragTransform.X * (bitmap.PixelSize.Width / Bounds.Width);
            var selectBoxWidth = SelectBox.Width * (bitmap.PixelSize.Width / Bounds.Width);
            if (selectBoxWidth + dragTransformX > bitmap.PixelSize.Width)
                cropW = bitmap.PixelSize.Width;
            else if (dragTransformX > 0)
                cropW = (int)selectBoxWidth;
            else cropW = (int)selectBoxWidth + (int)dragTransformX;
            var cropH = 0;
            var dragTransformY = SelectBox._dragTransform.Y * (bitmap.PixelSize.Height / Bounds.Height);
            var selectBoxHeight = SelectBox.Height * (bitmap.PixelSize.Height / Bounds.Height);
            if (selectBoxHeight + dragTransformY > bitmap.PixelSize.Height)
                cropH = bitmap.PixelSize.Height;
            else if (dragTransformY > 0)
                cropH = (int)selectBoxHeight;
            else cropH = (int)selectBoxHeight + (int)dragTransformY;
            var x = Math.Max((int)dragTransformX, 0);
            var y = Math.Max((int)dragTransformY, 0);

            if (selectMode && _currentWindowInfo.Hwnd != IntPtr.Zero)
            {
                return new ScreenCaptureInfo
                {
                    ScreenCaptureType = ScreenCaptureType.窗口,
                    WindowInfo = _currentWindowInfo
                };
            }
            
            // Logic to map to specific screen (copied from FinnishCapture)
            int absX = _screenCaptureInfo.ScreenInfo.X + x;
            int absY = _screenCaptureInfo.ScreenInfo.Y + y;
            
            var targetScreen = _screens.FirstOrDefault(s => 
                absX >= s.ScreenInfo.X && absX < s.ScreenInfo.X + s.ScreenInfo.Width &&
                absY >= s.ScreenInfo.Y && absY < s.ScreenInfo.Y + s.ScreenInfo.Height);

            if (targetScreen.Equals(default(ScreenCaptureInfo)))
            {
                targetScreen = _screens.FirstOrDefault();
            }
            
            if (!targetScreen.Equals(default(ScreenCaptureInfo)))
            {
                int relX = absX - targetScreen.ScreenInfo.X;
                int relY = absY - targetScreen.ScreenInfo.Y;
                    
                return new ScreenCaptureInfo
                {
                    ScreenCaptureType = ScreenCaptureType.屏幕,
                    X = relX,
                    Y = relY,
                    Width = cropW,
                    Height = cropH,
                    ScreenInfo = targetScreen.ScreenInfo
                };
            }
            else
            {
                return new ScreenCaptureInfo
                {
                    X = x,
                    Y = y,
                    Width = cropW,
                    Height = cropH,
                    ScreenInfo = _screenCaptureInfo.ScreenInfo
                };
            }
        }
        return new ScreenCaptureInfo();
    }

    private void FinnishCapture()
    {
        var info = GetSelectedScreenCaptureInfo();
        // Since GetSelectedScreenCaptureInfo handles the screen mapping, we just use the result.
        // But we need to handle the specific actions (Clipboard vs SelectMode).
        
        // Re-implementing the action logic using 'info' and the existing bitmap logic for clipboard/Crop
        // Note: The original code used renderTargetBitmap to crop the visual tree (including annotations).
        // GetSelectedScreenCaptureInfo only gives us the coordinates.
        // We still need to render the visual tree if we are saving/copying the annotated image.
        
        if (Image.Source is Bitmap bitmap)
        {
            // Calculate x,y,w,h again for RenderTargetBitmap (relative to the window/bitmap)
            // Or better, just reuse the logic for visual capture but use 'info' for the metadata.
            var cropW = 0;
            var scaleX = bitmap.PixelSize.Width / Bounds.Width;
            var dragTransformX = SelectBox._dragTransform.X * scaleX;
            var selectBoxWidth = SelectBox.Width * scaleX;
            if (selectBoxWidth + dragTransformX > bitmap.PixelSize.Width)
                cropW = bitmap.PixelSize.Width;
            else if (dragTransformX > 0)
                cropW = (int)selectBoxWidth;
            else cropW = (int)selectBoxWidth + (int)dragTransformX;
            var cropH = 0;
            var scaleY = bitmap.PixelSize.Height / Bounds.Height;
            var dragTransformY = SelectBox._dragTransform.Y * scaleY;
            var selectBoxHeight = SelectBox.Height * scaleY;
            if (selectBoxHeight + dragTransformY > bitmap.PixelSize.Height)
                cropH = bitmap.PixelSize.Height;
            else if (dragTransformY > 0)
                cropH = (int)selectBoxHeight;
            else cropH = (int)selectBoxHeight + (int)dragTransformY;
            var x = Math.Max((int)dragTransformX, 0);
            var y = Math.Max((int)dragTransformY, 0);

            if (selectMode)
            {
                selectModeAction.Invoke(info);
            }
            else
            {
                unsafe
                {
                    foreach (var canvasChild in Canvas.Children)
                        if (canvasChild is CaptureToolBase draggableResizeableControl)
                            draggableResizeableControl.IsSelected = false;
                    SelectBox.IsSelected = false;
                    ToolBar.IsVisible = false;
                    var renderTargetBitmap =
                        new RenderTargetBitmap(new PixelSize(bitmap.PixelSize.Width, bitmap.PixelSize.Height),
                            new Vector(96, 96));

                    var content = (Control)Content;
                    var transformGroup = new TransformGroup();
                    var scaleTransform = new ScaleTransform(scaleX, scaleY);
                    transformGroup.Children.Add(scaleTransform);
                    transformGroup.Children.Add(new TranslateTransform(0, 0));
                    content.RenderTransform = transformGroup;
                    content.Width = bitmap.PixelSize.Width;
                    content.Height = bitmap.PixelSize.Height;
                    content.Measure(Bounds.Size);
                    content.Arrange(new Rect(Bounds.Size));
                    renderTargetBitmap.Render(content);

                    var mat = new Mat(cropH, cropW, MatType.CV_8UC4);

                    renderTargetBitmap.CopyPixels(new PixelRect(x, y, cropW, cropH),
                        (IntPtr)mat.DataPointer,
                        cropW * cropH * 4,
                        (((int)cropW * PixelFormat.Rgba8888.BitsPerPixel + 31) & ~31) >> 3
                    );
                    if (selectBytesMode)
                        Task.Run(() =>
                        {
                            selectBytesModeAction.Invoke(new ScreenCaptureResult
                            {
                                Info = info,
                                Source = mat
                            });
                        });
                    else
                    {
                        ServiceManager.Services.GetService<IClipboardService>()!
                            .SetImageAsync(new ScreenCaptureResult
                            {
                                Info = info,
                                Source = mat
                            }).ContinueWith(e =>
                            {
                                mat.Dispose();
                                if (!e.Result)
                                {
                                    ServiceManager.Services.GetService<IToastService>().Show("截图失败", "无法复制到剪贴板",
                                        NotificationType.Error
                                    );
                                }
                            });
                    }

                    bitmap.Dispose();
                    renderTargetBitmap.Dispose();
                }
            }
        }

        Finish = true;
        Image.Source = null;

        WeakReferenceMessenger.Default.Send<string, string>("Close", "ScreenCapture");
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        if (Image.Source is Bitmap bitmap) bitmap.Dispose();

        Image.Source = null;

        WeakReferenceMessenger.Default.Send<string, string>("Close", "ScreenCapture");
        Close();
    }

    private void RectangleButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (lastTool is not null) lastTool.Classes.Remove("Selected");

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
        {
            NowTool = 截图工具.无;
        }
    }


    private void CircleButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (lastTool is not null) lastTool.Classes.Remove("Selected");

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
        {
            NowTool = 截图工具.无;
        }
    }

    private void ArrowButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (lastTool is not null) lastTool.Classes.Remove("Selected");

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
        {
            NowTool = 截图工具.无;
        }
    }

    private void TextButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (lastTool is not null) lastTool.Classes.Remove("Selected");

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
        {
            NowTool = 截图工具.无;
        }
    }

    private void CommentButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (lastTool is not null) lastTool.Classes.Remove("Selected");

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
        {
            NowTool = 截图工具.无;
        }
    }

    private void MosaicButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (lastTool is not null) lastTool.Classes.Remove("Selected");

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
        {
            NowTool = 截图工具.无;
        }
    }


    private void RedoButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Redo();
    }

    private bool Redo()
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
                            foreach (var resultPoint in item.points) MosaicCanvas.Points.Remove(resultPoint);

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

            return true;
        }

        return false;
    }

    private void ExButton_OnClick(object? sender, RoutedEventArgs e)
    {
        selectBytesMode = true;
        var screenCaptureExMethod = (ScreenCaptureExMethod)((Control)sender).DataContext;
        selectBytesModeAction = e =>
        {
            screenCaptureExMethod.Action.Invoke(e);
            e.Source?.Dispose();
        };
        FinnishCapture();
    }

    private enum SelectionState
    {
        None,
        WindowSelecting,
        MoveSelecting,
        Selected
    }
}