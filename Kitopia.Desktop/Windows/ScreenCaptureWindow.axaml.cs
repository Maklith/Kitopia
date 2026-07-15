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
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Utils;
using Kitopia.Desktop.Features.Utils.ImageTools;
using Kitopia.Desktop.Controls.Capture;
using Kitopia.Desktop.SDKs;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using PluginCore;
using PluginCore.ExMethod;
using Serilog;
using SharpHook;
using Ursa.Controls;
using Math = System.Math;
using MouseButton = Avalonia.Input.MouseButton;
using Point = Avalonia.Point;
using Rect = Avalonia.Rect;
using Size = Avalonia.Size;
using Window = Avalonia.Controls.Window;

namespace Kitopia.Desktop.Windows;

public partial class ScreenCaptureWindow : Window
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<ScreenCaptureWindow>();
    private WindowInfo _currentWindowInfo;
    private Point _pointerStartPoint;
    private readonly ScreenCaptureInfo _screenCaptureInfo;
    private Point _startPoint;
    private readonly List<WindowInfo> _windowInfos;
    private readonly List<ScreenCaptureInfo> _screens;
    private bool _isAddingTool;
    private bool _isFinished;

    private Button? _lastTool;
    private CaptureToolBase? _currentCaptureControl;

    private SelectionState _currentSelectionState = SelectionState.None;

    private 截图工具 _currentTool = 截图工具.无;
    public readonly Stack<ScreenCaptureRedoInfo> RedoStack = new();
    private RenderTargetBitmap? _renderTargetBitmap;
    private bool _selectBytesMode;
    private Action<ScreenCaptureResult>? _selectBytesModeAction;
    private Action? _selectBytesModeCancelAction;
    private bool _selectMode;
    private Action<ScreenCaptureInfo>? _selectModeAction;

    private readonly byte[]? _screenPixels;
    private readonly int _pixelWidth;
    private readonly int _pixelHeight;
    private readonly int _combinedMinX;
    private readonly int _combinedMinY;
    private readonly WriteableBitmap? _magnifierBmp;
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

        if (results.Count <= 0) {
            return;
        }

        if (!results.All(r => r.Info.ScreenInfo.HasValue)) {
            return;
        }

        minX = results.Min(r => r.Info.ScreenInfo!.Value.X);
        minY = results.Min(r => r.Info.ScreenInfo!.Value.Y);
        _combinedMinX = minX;
        _combinedMinY = minY;
        int maxX = results.Max(r => r.Info.ScreenInfo!.Value.X + r.Info.ScreenInfo!.Value.Width);
        int maxY = results.Max(r => r.Info.ScreenInfo!.Value.Y + r.Info.ScreenInfo!.Value.Height);
        width = maxX - minX;
        height = maxY - minY;

        if (width > 0 && height > 0) 
        {
            // Combined Mat
            using var combinedMat = new Mat(height, width, MatType.CV_8UC4, Scalar.All(0));
            
            foreach (var result in results)
            {
                if (result.Source is not { IsDisposed: false }) continue;
                int x = result.Info.ScreenInfo!.Value.X ;
                int y = result.Info.ScreenInfo!.Value.Y;
                    
                if (x-minX>=0&&y-minY>=0 && x + result.Source.Width <= width && y + result.Source.Height <= height)
                {
                    using var roiMat = combinedMat[new OpenCvSharp.Rect(x-minX, y-minY, result.Source.Width, result.Source.Height)];
                    result.Source.CopyTo(roiMat);
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
                ScreenInfo = new PluginCore.Rect(minX, minY, width, height),
            };

            Position = new PixelPoint(minX, minY);
            
            var scaling = 1.0;
            var screen = Screens.Primary;
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
        WindowDecorations = WindowDecorations.None;
        Background = new SolidColorBrush(Colors.Black);
        ShowInTaskbar = false;

        if (!Debugger.IsAttached) Topmost = true;


        CanResize = false;
        IsVisible = true;
        WeakReferenceMessenger.Default.Register<string, string>(this, "ScreenCapture", (_, message) =>
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
                    _currentSelectionState = SelectionState.Selected;
                    Cursor?.Dispose();
                    Cursor = Cursor.Default;


                    break;
                }
            }
        });
    }

    private bool _isLongCapturing;
    private bool _isWaitingForLongCaptureClick;
    private TaskCompletionSource? _longCaptureClickTaskSource;

    private async void LongCaptureButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_isLongCapturing) return;
            _isLongCapturing = true;

            try
            {
                // 1. Get current selection info
                var captureInfo = GetSelectedScreenCaptureInfo();
                if (!captureInfo.RequestRect.HasValue||captureInfo.RequestRect.Value.Width <= 0 || captureInfo.RequestRect.Value.Height <= 0) {
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
                var effectiveSelectionRect = GetEffectiveSelectRect();
                var progressWindow = new LongScreenshotProgressWindow
                {
                    Width = effectiveSelectionRect.Width,
                    Height = effectiveSelectionRect.Height,
                    Position = new PixelPoint(captureInfo.RequestRect.Value.X, captureInfo.RequestRect.Value.Y- captureInfo.RequestRect.Value.Height)
                };
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
                hook.MouseClicked += (_, _) => progressWindow.RequestStop();
                hook.KeyPressed += (_, _) => progressWindow.RequestStop();
                _ = hook.RunAsync();

                // 5. Scroll and Stitch Loop
                int maxScrolls = 50;
            
                double stepRatio = captureInfo.RequestRect.Value.Height / 600.0;
                int stepMagnitude = (int)(120 * stepRatio);
            
                if (stepMagnitude < 120) stepMagnitude = 120;
                if (stepMagnitude > 360) stepMagnitude = 360; 

                short scrollStep = (short)-stepMagnitude; 
            
                for (int i = 0; i < maxScrolls; i++)
                {
                    if (progressWindow.IsStopRequested) break;
                
                    simulator.SimulateMouseWheel(scrollStep);
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
            
                // 7. Finish
                await ServiceManager.Services.GetService<IClipboardService>()!
                    .SetImageAsync(new ScreenCaptureResult
                    {
                        Info = captureInfo, 
                        Source = accumulator.Clone() 
                    });
            
                accumulator.Dispose();
                this.Close();
                WeakReferenceMessenger.Default.Send<string, string>("Close", "ScreenCapture");
            
                ServiceManager.Services.GetService<IToastService>()!.Show("成功", "长截图已复制到剪贴板", NotificationType.Success);

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
        catch (Exception exception)
        {
            ServiceManager.Services.GetService<IToastService>()?.Show("错误", $"发生异常: {exception.Message}", NotificationType.Error);
            Logger.Error(exception, "长截图发生异常");
            
        }
    }

    public void SetToSelectMode(Action<ScreenCaptureInfo> selectModeAction)
    {
        _selectMode = true;
        this._selectModeAction = selectModeAction;
    }

    public void SetToSelectBytesMode(Action<ScreenCaptureResult> selectBytesModeAction,
        Action selectBytesModeCancelAction)
    {
        _selectBytesMode = true;
        this._selectBytesModeAction = selectBytesModeAction;
        this._selectBytesModeCancelAction = selectBytesModeCancelAction;
    }


    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        ColorPicker.PaletteColors =
        [
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
        ];
        ColorPicker.Color = Colors.Red;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        SelectBox.LocationOrSizeChanged += LocationOrSizeChanged;
        StrokeWidth.ValueChanged += StrokeWidthOnValueChanged;
        StrokeWidth2.ValueChanged += StrokeWidth2OnValueChanged;

    }

    private void StrokeWidth2OnValueChanged(object? sender, ValueChangedEventArgs<int> e) {
        StrokeWidth.Value = (double)e.NewValue! ;
    }


    protected override void OnClosed(EventArgs e)
    {
        SelectBox.LocationOrSizeChanged -= LocationOrSizeChanged;
        StrokeWidth.ValueChanged -= StrokeWidthOnValueChanged;
        StrokeWidth2.ValueChanged -= StrokeWidth2OnValueChanged;
        _renderTargetBitmap?.Dispose();
        MosaicImage.OpacityMask = null;

        if (_selectBytesMode && !_isFinished) _selectBytesModeCancelAction?.Invoke();

        base.OnClosed(e);
    }



    private void UpdateBrushCursor()
    {
        if (_currentSelectionState != SelectionState.Selected) return;

        if (_currentTool is 截图工具.马赛克 or 截图工具.批准)
        {
            var scaling = 1.0;
            var screen = Screens.ScreenFromPoint(Position);
            if (screen != null) scaling = screen.Scaling;

            // 画笔真实直径
            double diameter = StrokeWidth.Value + (_currentTool == 截图工具.马赛克 ? 5 : 0);
            // 光标边框厚度
            double cursorStrokeThickness = 2;
            
            // 为了防止边框被裁剪，逻辑边界需要比真实直径大一个边框厚度（因为 Stroke 是居中绘制的）
            double logicalSize = diameter + cursorStrokeThickness;
            
            var pixelSize = (int)Math.Round(logicalSize * scaling, MidpointRounding.AwayFromZero);
            if (pixelSize < 1) pixelSize = 1;

            var renderTargetBitmap = new RenderTargetBitmap(new PixelSize(pixelSize, pixelSize), new Vector(96 * scaling, 96 * scaling));
            var ellipse = new Ellipse
            {
                Stroke = new SolidColorBrush(ColorPicker.Color),
                StrokeThickness = cursorStrokeThickness,
                Width = diameter,
                Height = diameter
            };

            // 测量和排列使用逻辑总大小，这样圆形就能居中且边框完全保留
            ellipse.Measure(new Size(logicalSize, logicalSize));
            ellipse.Arrange(new Rect(new Point(0, 0), new Size(logicalSize, logicalSize)));
            renderTargetBitmap.Render(ellipse);
            
            SelectBox.Cursor?.Dispose();
            SelectBox.Cursor = new Cursor(renderTargetBitmap, new PixelPoint(pixelSize / 2, pixelSize / 2));
        }
        else
        {
            SelectBox.Cursor?.Dispose();
            SelectBox.Cursor = new Cursor(StandardCursorType.SizeAll);
        }
    }

    private void StrokeWidthOnValueChanged(object? sender, RangeBaseValueChangedEventArgs valueChangedEventArgs)
    {
        var newValue = valueChangedEventArgs.NewValue;

        if (_currentTool == 截图工具.马赛克)
        {
            MosaicCanvas.StrokeThickness = 5 + newValue;
            _renderTargetBitmap?.Render(MosaicCanvas);
        }
        
        UpdateBrushCursor();
    }

    private void LocationOrSizeChanged(object? sender, LocationOrSizeChangedEventArgs locationOrSizeChangedEventArgs)
    {
        UpdateSelectBox();
        UpdateToolBar();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            if (_currentSelectionState == SelectionState.Selected)
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
        if (_currentSelectionState == SelectionState.Selected) return;
        if (_currentSelectionState == SelectionState.MoveSelecting) _startPoint = _pointerStartPoint;
        _currentSelectionState = SelectionState.Selected;
        if (SelectBox.Height < 10) SelectBox.Height = 10;

        if (SelectBox.Width < 10) SelectBox.Width = 10;

        SelectBox.IsVisible = true;
        SelectBox.ShowDragThumbs = true;
        if (Cursor?.ToString() != "Default")
        {
            Cursor?.Dispose();
            Cursor = Cursor.Default;
        }

        WeakReferenceMessenger.Default.Send<string, string>("Selected", "ScreenCapture");
        UpdateSelectBox();

        if (ConfigManger.Config.截图直接复制到剪贴板 || _selectBytesMode || _selectMode)
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

        if (_currentSelectionState == SelectionState.Selected) return;
        if (e.GetCurrentPoint(this)
            .Properties.IsLeftButtonPressed)
        {
            _currentSelectionState = SelectionState.WindowSelecting;
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
        {
            if (_currentSelectionState == SelectionState.None)
                WeakReferenceMessenger.Default.Send<string, string>("Close", "ScreenCapture");
        }
        
        if (_currentSelectionState == SelectionState.Selected) return;
        CompletedSelection();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (_currentSelectionState == SelectionState.Selected) return;
        
        _currentSelectionState = SelectionState.WindowSelecting;
        SelectWindow(e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (_currentSelectionState != SelectionState.None) return;

        if (_currentSelectionState != SelectionState.Selected) _currentSelectionState = SelectionState.None;

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

        if (_currentSelectionState == SelectionState.Selected) return;

        if (_currentSelectionState == SelectionState.None) _currentSelectionState = SelectionState.WindowSelecting;
        var position = e.GetPosition(this);

        if (e.Properties.IsLeftButtonPressed && _currentSelectionState is SelectionState.WindowSelecting
                                             && (position.Y - _startPoint.Y) * (position.Y - _startPoint.Y) +
                                             (position.X - _startPoint.X) * (position.X - _startPoint.X) > 1300)
        {
            _currentSelectionState = SelectionState.MoveSelecting;
            _startPoint = _pointerStartPoint;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && _currentSelectionState == SelectionState.MoveSelecting)
        {
                var selectBoxHeight = e.GetPosition(this)
                    .Y - _startPoint.Y;
                var selectBoxWidth = e.GetPosition(this)
                    .X - _startPoint.X;
                if (selectBoxWidth<15 && selectBoxHeight<15) {
                    return;
                }
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

        if (_currentSelectionState == SelectionState.WindowSelecting) SelectWindow(e);
    }

    private void SelectWindow(PointerEventArgs e)
    {
        var currentPoint = e.GetCurrentPoint(this);
        var pixelPoint = this.PointToScreen(currentPoint.Position);
        var positionX = pixelPoint.X;
        var positionY = pixelPoint.Y;

        var windowInfoList = _windowInfos.Where(windowInfo => positionX >= windowInfo.Rect.X && positionX <= windowInfo.Rect.X + windowInfo.Rect.Width &&
                                                     positionY >= windowInfo.Rect.Y && positionY <= windowInfo.Rect.Y + windowInfo.Rect.Height)
            .OrderBy(windowInfo => windowInfo.ZIndex).ToList();
        
        Rect targetRectPhysical;

        if (windowInfoList.Count == 0)
        {
            // Fallback: Check which screen we are on
            var screen = _screens.FirstOrDefault(s => s.ScreenInfo.HasValue&&positionX >= s.ScreenInfo.Value.X && 
                                                      positionX < s.ScreenInfo.Value.X + s.ScreenInfo.Value.Width &&
                                                      positionY >= s.ScreenInfo.Value.Y && positionY < s.ScreenInfo.Value.Y + s.ScreenInfo.Value.Height);

            if (!screen.Equals(default)&&screen.ScreenInfo.HasValue)
            {
                // Found a screen
                targetRectPhysical = new Rect(screen.ScreenInfo.Value.X, screen.ScreenInfo.Value.Y, screen.ScreenInfo.Value.Width, screen.ScreenInfo.Value.Height);
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
            var windowInfo = windowInfoList.First();
            _currentWindowInfo = windowInfo;
            targetRectPhysical = new Rect(windowInfo.Rect.X, windowInfo.Rect.Y, windowInfo.Rect.Width, windowInfo.Rect.Height);
        }
        
        // Convert Physical Rect back to Logical coordinates for SelectBox
        var topLeft = this.PointToClient(new PixelPoint((int)targetRectPhysical.X, (int)targetRectPhysical.Y));
        var bottomRight = this.PointToClient(new PixelPoint((int)(targetRectPhysical.X + targetRectPhysical.Width), (int)(targetRectPhysical.Y + targetRectPhysical.Height)));

        // Handle negative coordinates or off-canvas mapping if necessary, though PointToClient should handle it relative to window origin
        var width = bottomRight.X - topLeft.X;
        var height = bottomRight.Y - topLeft.Y;
        var displayRect = ScreenCaptureSelectionGeometry.GetDisplayRectForContentRect(new Rect(topLeft.X, topLeft.Y, width, height));
        
        _startPoint = displayRect.Position;
        SelectBox._dragTransform.X = displayRect.X;
        SelectBox._dragTransform.Y = displayRect.Y;
        SelectBox.Width = displayRect.Width;
        SelectBox.Height = displayRect.Height;

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

            switch (_currentTool)
            {
                case 截图工具.无:
                {
                    return;
                }
                case 截图工具.矩形:
                {
                    var position = e.GetPosition(this);
                    _startPoint = position;
                    var dragger = new DraggableResizeableControl
                    {
                        _dragTransform =
                        {
                            X = position.X,
                            Y = position.Y
                        },
                        IsSelected = true,
                        Width = 5,
                        Height = 5
                    };
                    var rectangle = new Rectangle();
                    dragger.Content = rectangle;

                    rectangle.Stroke = new SolidColorBrush(ColorPicker.Color);
                    rectangle.StrokeThickness = StrokeWidth.Value;

                    Canvas.Children.Add(dragger);
                    _isAddingTool = true;
                    _currentCaptureControl = dragger;

                    dragger.Focus();
                    break;
                }
                case 截图工具.圆形:
                {
                    var position = e.GetPosition(this);
                    _startPoint = position;
                    var dragger = new DraggableResizeableControl
                    {
                        _dragTransform =
                        {
                            X = position.X,
                            Y = position.Y
                        },
                        Width = 5,
                        Height = 5
                    };
                    var rectangle = new Ellipse();
                    dragger.Content = rectangle;
                    dragger.IsSelected = true;
                    rectangle.Stroke = new SolidColorBrush(ColorPicker.Color);
                    rectangle.StrokeThickness = StrokeWidth.Value;

                    Canvas.Children.Add(dragger);
                    _isAddingTool = true;
                    _currentCaptureControl = dragger;
                    dragger.Focus();
                    break;
                }
                case 截图工具.箭头:
                {
                    var position = e.GetPosition(this);
                    _startPoint = position;
                    var dragger = new DraggableArrowControl
                    {
                        IsSelected = true,
                        Source = position,
                        Target = position,
                        Stroke = new SolidColorBrush(ColorPicker.Color),
                        Fill = new SolidColorBrush(ColorPicker.Color),
                        StrokeThickness = StrokeWidth.Value,
                        Width = Width,
                        Height = Height
                    };
                    dragger.ArrowSize = new Size(8 * dragger.StrokeThickness, 8 * dragger.StrokeThickness);
                    Canvas.Children.Add(dragger);
                    _isAddingTool = true;
                    _currentCaptureControl = dragger;
                    dragger.Focus();
                    break;
                }
                case 截图工具.批准:
                {
                    var position = e.GetPosition(this);
                    _startPoint = position;

                    var rectangle = new PenCaptureTool();
                    rectangle.Points.Add(position);
                    rectangle.StrokeThickness = StrokeWidth.Value;
                    rectangle.Stroke = new SolidColorBrush(ColorPicker.Color);
                    rectangle.Fill = new SolidColorBrush(ColorPicker.Color);
                    rectangle.Width = Width;
                    rectangle.Height = Height;
                    Canvas.Children.Add(rectangle);
                    _isAddingTool = true;
                    _currentCaptureControl = rectangle;
                    rectangle.Focus();
                    break;
                }
                case 截图工具.文本:
                {
                    var position = e.GetPosition(this);
                    _startPoint = position;
                    var dragger = new TextCaptureTool
                    {
                        IsRedoing = true,
                        _dragTransform =
                        {
                            X = position.X,
                            Y = position.Y
                        },
                        IsSelected = true,
                        Foreground = new SolidColorBrush(ColorPicker.Color),
                        Text = "文本1",
                        FontSize = 13 + StrokeWidth.Value
                    };


                    Canvas.Children.Add(dragger);
                    _isAddingTool = true;
                    _currentCaptureControl = dragger;
                    dragger.Focus();
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
                                        if (writeableBitmap.Format != null)
                                            writeableBitmap.CopyPixels(source, (IntPtr)p,
                                                source.Width * source.Height * 4,
                                                ((source.Width * writeableBitmap.Format.Value.BitsPerPixel + 31) &
                                                 ~31) >>
                                                3);
                                    }

                                    var process = GaussianBlur1.GaussianBlur(bytes, source.Width,
                                        source.Height, ConfigManger.Config.GaussianBlurRadius);
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

                            _renderTargetBitmap = new RenderTargetBitmap(new PixelSize((int)Width, (int)Height),
                                new Vector(96, 96));

                            var brush = new ImageBrush(_renderTargetBitmap);
                            MosaicImage.OpacityMask = brush;
                            _renderTargetBitmap?.Render(MosaicCanvas);
                        });
                    _startPoint = position;
                    RedoStack.Push(new ScreenCaptureRedoInfo
                    {
                        EditType = ScreenCaptureEditType.移动,
                        Type = 截图工具.马赛克,
                        Points = new List<Point> { position }
                    });
                    MosaicCanvas.Points.Add(position);
                    MosaicCanvas.StrokeThickness = 5 + StrokeWidth.Value;
                    _isAddingTool = true;

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

        if (_currentSelectionState == SelectionState.Selected)
        {
            if (!(StrokeWidth.Value > 1)) StrokeWidth.Value = 1;

            switch (_currentTool)
            {
                case 截图工具.无:
                {
                    if (SelectBox.Cursor?.ToString() != "SizeAll")
                    {
                        SelectBox.Cursor?.Dispose();
                        SelectBox.Cursor = new Cursor(StandardCursorType.SizeAll);
                    }

                    break;
                }
                case 截图工具.马赛克:
                case 截图工具.批准:
                {
                    UpdateBrushCursor();
                    break;
                }
                default:
                {
                    if (SelectBox.Cursor?.ToString() != "Cross")
                    {
                        SelectBox.Cursor?.Dispose();
                        SelectBox.Cursor = new Cursor(StandardCursorType.Cross);
                    }

                    break;
                }
            }
        }

        if (!_isAddingTool) return;

        if (_currentTool == 截图工具.文本) return;

        if (_currentTool == 截图工具.箭头)
        {
            if (_currentCaptureControl is DraggableArrowControl arrow)
                arrow.Target = e.GetPosition(this);
        }
        else if (_currentTool == 截图工具.批准)
        {
            if (_currentCaptureControl is PenCaptureTool pen)
                pen.Points.Add(e.GetPosition(this));
        }
        else if (_currentTool == 截图工具.马赛克)
        {
            if (RedoStack.TryPeek(out var result))
            {
                if (result.Type != 截图工具.马赛克)
                    RedoStack.Push(new ScreenCaptureRedoInfo
                    {
                        EditType = ScreenCaptureEditType.移动,
                        Type = 截图工具.马赛克,
                        Points = new List<Point> { e.GetPosition(this) }
                    });
                else
                    RedoStack.Peek()
                        .Points?.Add(e.GetPosition(this));
            }
            else
            {
                RedoStack.Push(new ScreenCaptureRedoInfo
                {
                    EditType = ScreenCaptureEditType.移动,
                    Type = 截图工具.马赛克,
                    Points = new List<Point> { e.GetPosition(this) }
                });
            }


            MosaicCanvas.Points.Add(e.GetPosition(this));
            _renderTargetBitmap?.Render(MosaicCanvas);
        }
        else
        {
            var selectBoxHeight = e.GetPosition(this)
                .Y - _startPoint.Y;
            var selectBoxWidth = e.GetPosition(this)
                .X - _startPoint.X;

            if (selectBoxHeight < 0)
            {
                _currentCaptureControl!.Height = -selectBoxHeight;
                if (_currentCaptureControl is DraggableResizeableControl dragControl)
                    dragControl._dragTransform.Y = _startPoint.Y + selectBoxHeight;
            }
            else
            {
                _currentCaptureControl!.Height = selectBoxHeight;
                if (_currentCaptureControl is DraggableResizeableControl dragControl)
                    dragControl._dragTransform.Y = _startPoint.Y;
            }


            if (selectBoxWidth < 0)
            {
                _currentCaptureControl.Width = -selectBoxWidth;
                if (_currentCaptureControl is DraggableResizeableControl dragControl)
                    dragControl._dragTransform.X = _startPoint.X + selectBoxWidth;
            }
            else
            {
                _currentCaptureControl.Width = selectBoxWidth;
                if (_currentCaptureControl is DraggableResizeableControl dragControl)
                    dragControl._dragTransform.X = _startPoint.X;
            }
        }

        e.Handled = true;
    }

    private void SelectBox_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && _isAddingTool)
        {
            if (_currentTool != 截图工具.马赛克 && _currentCaptureControl != null)
                RedoStack.Push(new ScreenCaptureRedoInfo
                {
                    Type = _currentTool,
                    Target = _currentCaptureControl,
                    EditType = ScreenCaptureEditType.添加,
                    StartPoint = _startPoint,
                    Size = _currentCaptureControl.DesiredSize,
                    Points = null
                });

            if (_currentTool == 截图工具.马赛克)
            {
                if (RedoStack.TryPeek(out var result))
                {
                    if (result.Type != 截图工具.马赛克)
                        RedoStack.Push(new ScreenCaptureRedoInfo
                        {
                            EditType = ScreenCaptureEditType.移动,
                            Type = 截图工具.马赛克,
                            Points = new List<Point> { e.GetPosition(this) }
                        });
                }
                else
                {
                    RedoStack.Push(new ScreenCaptureRedoInfo
                    {
                        EditType = ScreenCaptureEditType.移动,
                        Type = 截图工具.马赛克,
                        Points = new List<Point> { e.GetPosition(this) }
                    });
                }


                MosaicCanvas.Points.Add(new Point(-1, -1));

                _renderTargetBitmap?.Render(MosaicCanvas);
            }

            _isAddingTool = false;
            e.Handled = true;
        }
    }

    private void SelectBox_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_isAddingTool)
        {
            if (_currentTool != 截图工具.马赛克 && _currentCaptureControl != null)
                RedoStack.Push(new ScreenCaptureRedoInfo
                {
                    Type = _currentTool,
                    Target = _currentCaptureControl,
                    EditType = ScreenCaptureEditType.添加,
                    StartPoint = _startPoint,
                    Size = _currentCaptureControl.DesiredSize,
                    Points = null
                });

            if (_currentTool == 截图工具.马赛克)
            {
                if (RedoStack.TryPeek(out var result))
                    if (result.Type == 截图工具.马赛克)
                        RedoStack.Peek()
                            .Points?.Add(new Point(-1, -1));


                MosaicCanvas.Points.Add(new Point(-1, -1));
                _renderTargetBitmap?.Render(MosaicCanvas);
            }

            _isAddingTool = false;
            e.Handled = true;
        }
    }


    private void UpdateSelectBox()
    {
        var selectionRect = GetEffectiveSelectRect();
        var fullScreenRect = new RectangleGeometry
        {
            Rect = new Rect(0, 0, Bounds.Width, Bounds.Height)
        };
        var selectionGeometry = new RectangleGeometry
        {
            Rect = selectionRect
        };


        var combinedGeometry = new CombinedGeometry
        {
            Geometry1 = fullScreenRect,
            Geometry2 = selectionGeometry,
            GeometryCombineMode = GeometryCombineMode.Exclude
        };

        Rectangle.Clip = combinedGeometry;
        Rectangle.InvalidateVisual();
        //Console.WriteLine("SelectBox: " + SelectBox._dragTransform.X + ", " + SelectBox._dragTransform.Y + ", " + SelectBox.Width + ", " + SelectBox.Height);
    }

    private void UpdateToolBar()
    {
        var selectionRect = GetEffectiveSelectRect();
        ToolBar.IsVisible = true;
        ToolBar.Measure(Bounds.Size);
        var margin = 5.0;
        var toolBarWidth = ToolBar.DesiredSize.Width;
        var toolBarHeight = ToolBar.DesiredSize.Height;

        var selCenterLogical = new Point(selectionRect.X + selectionRect.Width / 2,
                                         selectionRect.Y + selectionRect.Height / 2);
        var selCenterPhysical = this.PointToScreen(selCenterLogical);
        var targetScreen = Screens.ScreenFromPoint(selCenterPhysical);

        double minXLogical, minYLogical, maxXLogical, maxYLogical;

        if (targetScreen != null)
        {
            var topLeftLogical = this.PointToClient(new PixelPoint(targetScreen.Bounds.X, targetScreen.Bounds.Y));
            var bottomRightLogical = this.PointToClient(new PixelPoint(targetScreen.Bounds.X + targetScreen.Bounds.Width,
                targetScreen.Bounds.Y + targetScreen.Bounds.Height));
            minXLogical = Math.Min(topLeftLogical.X, bottomRightLogical.X);
            minYLogical = Math.Min(topLeftLogical.Y, bottomRightLogical.Y);
            maxXLogical = Math.Max(topLeftLogical.X, bottomRightLogical.X);
            maxYLogical = Math.Max(topLeftLogical.Y, bottomRightLogical.Y);
        }
        else
        {
            minXLogical = 0;
            minYLogical = 0;
            maxXLogical = Bounds.Width;
            maxYLogical = Bounds.Height;
        }

        var left = selectionRect.X + selectionRect.Width + margin;
        var top = selectionRect.Y + selectionRect.Height + margin;

        if (left + toolBarWidth + margin > maxXLogical)
        {
            left = maxXLogical - toolBarWidth - margin;
        }
        if (left < minXLogical + margin) left = minXLogical + margin;

        if (top + toolBarHeight + margin > maxYLogical)
        {
            var topAbove = selectionRect.Y - toolBarHeight - margin;
            if (topAbove >= minYLogical + margin)
            {
                top = topAbove;
            }
            else
            {
                top = maxYLogical - toolBarHeight - margin;
            }
        }
        if (top < minYLogical + margin) top = minYLogical + margin;

        if (left + toolBarWidth > Bounds.Width) left = Math.Max(0, Bounds.Width - toolBarWidth);
        if (top + toolBarHeight > Bounds.Height) top = Math.Max(0, Bounds.Height - toolBarHeight);
        if (left < 0) left = 0;
        if (top < 0) top = 0;

        ToolBar.SetValue(Canvas.LeftProperty, left);
        ToolBar.SetValue(Canvas.TopProperty, top);
    }

    private void Rectangle_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (_currentSelectionState == SelectionState.Selected)
            if (Cursor?.ToString() != "Default")
            {
                Cursor?.Dispose();
                Cursor = Cursor.Default;
            }
    }


    private void UpdateColorInspector(Point p)
    {
        if (_screenPixels == null || ColorInspector == null || _pixelWidth == 0 || _pixelHeight == 0) return;
        
        // Don't show if selecting
        if (_currentSelectionState != SelectionState.None && _currentSelectionState != SelectionState.WindowSelecting) 
        {
             ColorInspector.IsVisible = false;
             return;
        }

        var imagePoint = this.TranslatePoint(p, Image);
        if (imagePoint == null || Image.Bounds.Width <= 0 || Image.Bounds.Height <= 0)
        {
            ColorInspector.IsVisible = false;
            return;
        }

        int centerX = (int)(imagePoint.Value.X * _pixelWidth / Image.Bounds.Width);
        int centerY = (int)(imagePoint.Value.Y * _pixelHeight / Image.Bounds.Height);

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
        const double popupOffset = 20;
        ColorInspector.Measure(Bounds.Size);
        double inspectorWidth = ColorInspector.DesiredSize.Width;
        double inspectorHeight = ColorInspector.DesiredSize.Height;

        var pointerPhysical = this.PointToScreen(p);
        var targetScreen = Screens.ScreenFromPoint(pointerPhysical);
        double minXLogical = 0;
        double minYLogical = 0;
        double maxXLogical = Bounds.Width;
        double maxYLogical = Bounds.Height;

        if (targetScreen != null)
        {
            var topLeftLogical = this.PointToClient(new PixelPoint(targetScreen.Bounds.X, targetScreen.Bounds.Y));
            var bottomRightLogical = this.PointToClient(new PixelPoint(
                targetScreen.Bounds.X + targetScreen.Bounds.Width,
                targetScreen.Bounds.Y + targetScreen.Bounds.Height));
            minXLogical = Math.Min(topLeftLogical.X, bottomRightLogical.X);
            minYLogical = Math.Min(topLeftLogical.Y, bottomRightLogical.Y);
            maxXLogical = Math.Max(topLeftLogical.X, bottomRightLogical.X);
            maxYLogical = Math.Max(topLeftLogical.Y, bottomRightLogical.Y);
        }

        double popupX = p.X + popupOffset;
        double popupY = p.Y + popupOffset;

        if (popupX + inspectorWidth > maxXLogical) popupX = p.X - inspectorWidth - popupOffset;
        if (popupY + inspectorHeight > maxYLogical) popupY = p.Y - inspectorHeight - popupOffset;

        popupX = Math.Clamp(popupX, minXLogical, Math.Max(minXLogical, maxXLogical - inspectorWidth));
        popupY = Math.Clamp(popupY, minYLogical, Math.Max(minYLogical, maxYLogical - inspectorHeight));

        Canvas.SetLeft(ColorInspector, popupX);
        Canvas.SetTop(ColorInspector, popupY);
        ColorInspector.IsVisible = true;
    }

    private void SaveToClipboard_Click(object? sender, RoutedEventArgs e)
    {
        FinnishCapture();

        Close();
    }

    private bool TryGetSelectedBitmapRect(out PixelRect cropRect, out PluginCore.Rect absoluteRect)
    {
        cropRect = default;
        absoluteRect = default;

        if (Image.Source is not Bitmap bitmap || !_screenCaptureInfo.ScreenInfo.HasValue)
            return false;

        var selectionRect = GetEffectiveSelectRect();
        var start = this.PointToScreen(new Point(selectionRect.X, selectionRect.Y));
        var end = this.PointToScreen(new Point(selectionRect.X + selectionRect.Width, selectionRect.Y + selectionRect.Height));

        int absLeft = Math.Min(start.X, end.X);
        int absTop = Math.Min(start.Y, end.Y);
        int absRight = Math.Max(start.X, end.X);
        int absBottom = Math.Max(start.Y, end.Y);

        int combinedLeft = _combinedMinX;
        int combinedTop = _combinedMinY;
        int combinedRight = combinedLeft + bitmap.PixelSize.Width;
        int combinedBottom = combinedTop + bitmap.PixelSize.Height;

        int clampedLeft = Math.Clamp(absLeft, combinedLeft, combinedRight);
        int clampedTop = Math.Clamp(absTop, combinedTop, combinedBottom);
        int clampedRight = Math.Clamp(absRight, combinedLeft, combinedRight);
        int clampedBottom = Math.Clamp(absBottom, combinedTop, combinedBottom);

        int width = clampedRight - clampedLeft;
        int height = clampedBottom - clampedTop;
        if (width <= 0 || height <= 0)
            return false;

        int bitmapX = clampedLeft - combinedLeft;
        int bitmapY = clampedTop - combinedTop;

        cropRect = new PixelRect(bitmapX, bitmapY, width, height);
        absoluteRect = new PluginCore.Rect(clampedLeft, clampedTop, width, height);
        return true;
    }

    private ScreenCaptureInfo GetSelectedScreenCaptureInfo()
    {
        if (TryGetSelectedBitmapRect(out _, out var absoluteRect))
        {
            if (_selectMode && _currentWindowInfo.Hwnd != IntPtr.Zero)
            {
                int absX = absoluteRect.X;
                int absY = absoluteRect.Y;
            
                var targetScreen = _screens.FirstOrDefault(s => 
                    s.ScreenInfo != null &&
                    absX >= s.ScreenInfo.Value.X && absX < s.ScreenInfo.Value.X + s.ScreenInfo.Value.Width &&
                    absY >= s.ScreenInfo.Value.Y && absY < s.ScreenInfo.Value.Y + s.ScreenInfo.Value.Height);
                return new ScreenCaptureInfo
                {
                    ScreenCaptureType = ScreenCaptureType.窗口,
                    ScreenInfo = targetScreen.ScreenInfo,
                    WindowInfo = _currentWindowInfo,
                    RequestRect = _currentWindowInfo.Rect
                };
            }
            
            // Logic to map to specific screen (copied from FinnishCapture)
            if (_screenCaptureInfo.ScreenInfo != null) {
                int absX = absoluteRect.X;
                int absY = absoluteRect.Y;
            
                var targetScreen = _screens.FirstOrDefault(s => 
                    s.ScreenInfo != null &&
                    absX >= s.ScreenInfo.Value.X && absX < s.ScreenInfo.Value.X + s.ScreenInfo.Value.Width &&
                    absY >= s.ScreenInfo.Value.Y && absY < s.ScreenInfo.Value.Y + s.ScreenInfo.Value.Height);

                if (targetScreen.Equals(default))
                {
                    return new ScreenCaptureInfo
                    {
                        RequestRect = absoluteRect,
                        ScreenInfo = _screenCaptureInfo.ScreenInfo
                    };
                }
                if (targetScreen.ScreenInfo != null) {
                    int relX = absX - targetScreen.ScreenInfo.Value.X;
                    int relY = absY - targetScreen.ScreenInfo.Value.Y;
                    
                    return new ScreenCaptureInfo
                    {
                        ScreenCaptureType = ScreenCaptureType.屏幕,
                        RequestRect = new PluginCore.Rect(relX, relY, absoluteRect.Width, absoluteRect.Height),
                        ScreenInfo = targetScreen.ScreenInfo
                    };
                }
            }
        }
        return new ScreenCaptureInfo();
    }

    private Rect GetEffectiveSelectRect()
    {
        return ScreenCaptureSelectionGeometry.GetContentRectForDisplayRect(
            new Rect(SelectBox._dragTransform.X, SelectBox._dragTransform.Y, SelectBox.Width, SelectBox.Height));
    }

    private void FinnishCapture()
    {
        var info = GetSelectedScreenCaptureInfo();
        if (Image.Source is Bitmap bitmap)
        {
            if (!TryGetSelectedBitmapRect(out var cropRect, out _))
            {
                _isFinished = true;
                Image.Source = null;
                WeakReferenceMessenger.Default.Send<string, string>("Close", "ScreenCapture");
                return;
            }

            if (_selectMode)
            {
                _selectModeAction?.Invoke(info);
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

                    var content = (Control)Content!;
                    var transformGroup = new TransformGroup();
                    var scaleTransform = new ScaleTransform(bitmap.PixelSize.Width / Bounds.Width, bitmap.PixelSize.Height / Bounds.Height);
                    transformGroup.Children.Add(scaleTransform);
                    transformGroup.Children.Add(new TranslateTransform(0, 0));
                    content.RenderTransform = transformGroup;
                    content.Width = bitmap.PixelSize.Width;
                    content.Height = bitmap.PixelSize.Height;
                    content.Measure(Bounds.Size);
                    content.Arrange(new Rect(Bounds.Size));
                    renderTargetBitmap.Render(content);

                    var mat = new Mat(cropRect.Height, cropRect.Width, MatType.CV_8UC4);

                    renderTargetBitmap.CopyPixels(cropRect,
                        (IntPtr)mat.DataPointer,
                        cropRect.Width * cropRect.Height * 4,
                        ((cropRect.Width * PixelFormat.Rgba8888.BitsPerPixel + 31) & ~31) >> 3
                    );
                    if (_selectBytesMode)
                        Task.Run(() =>
                        {
                            _selectBytesModeAction?.Invoke(new ScreenCaptureResult
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
                                    ServiceManager.Services.GetService<IToastService>()!.Show("截图失败", "无法复制到剪贴板",
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

        _isFinished = true;
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
        if (_lastTool is not null) _lastTool.Classes.Remove("Selected");

        if (_currentTool != 截图工具.矩形)
        {
            _currentTool = 截图工具.矩形;
            if (sender is not Button button) return;
            _lastTool = button;
            _lastTool.Classes.Add("Selected");
        }
        else
        {
            _currentTool = 截图工具.无;
        }
    }


    private void CircleButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_lastTool is not null) _lastTool.Classes.Remove("Selected");

        if (_currentTool != 截图工具.圆形)
        {
            _currentTool = 截图工具.圆形;
            if (sender is not Button button) return;
            _lastTool = button;
            _lastTool.Classes.Add("Selected");
        }
        else
        {
            _currentTool = 截图工具.无;
        }
    }

    private void ArrowButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _lastTool?.Classes.Remove("Selected");

        if (_currentTool != 截图工具.箭头)
        {
            _currentTool = 截图工具.箭头;
            if (sender is not Button button) return;
            _lastTool = button;
            _lastTool.Classes.Add("Selected");
        }
        else
        {
            _currentTool = 截图工具.无;
        }
    }

    private void TextButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _lastTool?.Classes.Remove("Selected");

        if (_currentTool != 截图工具.文本)
        {
            _currentTool = 截图工具.文本;
            if (sender is not Button button) return;
            _lastTool = button;
            _lastTool.Classes.Add("Selected");
        }
        else
        {
            _currentTool = 截图工具.无;
        }
    }

    private void CommentButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _lastTool?.Classes.Remove("Selected");

        if (_currentTool != 截图工具.批准)
        {
            _currentTool = 截图工具.批准;
            if (sender is not Button button) return;
            _lastTool = button;
            _lastTool.Classes.Add("Selected");
        }
        else
        {
            _currentTool = 截图工具.无;
        }
    }

    private void MosaicButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_lastTool is not null) _lastTool.Classes.Remove("Selected");

        if (_currentTool != 截图工具.马赛克)
        {
            _currentTool = 截图工具.马赛克;
            if (sender is not Button button) return;
            _lastTool = button;
            _lastTool.Classes.Add("Selected");
        }
        else
        {
            _currentTool = 截图工具.无;
        }
    }


    private void RedoButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Redo();
    }

    private bool Redo()
    {
        if (RedoStack.TryPop(out var item))
        {
            switch (item.EditType)
            {
                case ScreenCaptureEditType.添加:
                {
                    if (item.Target is Control targetControl)
                        Canvas.Children.Remove(targetControl);
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
                                SelectBox._dragTransform.X = item.StartPoint.X;
                                SelectBox._dragTransform.Y = item.StartPoint.Y;
                                UpdateSelectBox();
                                UpdateToolBar();
                            }
                            else if (item.Target is DraggableResizeableControl draggable)
                            {
                                draggable._dragTransform.X = item.StartPoint.X;
                                draggable._dragTransform.Y = item.StartPoint.Y;
                            }

                            break;
                        }
                        case 截图工具.箭头:
                        {
                            if (item.Target is DraggableArrowControl arrow)
                            {
                                arrow.Source = item.Point1;
                                arrow.Target = item.Point2;
                            }

                            break;
                        }

                        case 截图工具.马赛克:
                        {
                            if (item.Points != null)
                            {
                                foreach (var resultPoint in item.Points) MosaicCanvas.Points.Remove(resultPoint);

                                item.Points.Clear();
                                item.Points = null;
                            }
                            _renderTargetBitmap?.Render(MosaicCanvas);
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
                                SelectBox._dragTransform.X = item.StartPoint.X;
                                SelectBox._dragTransform.Y = item.StartPoint.Y;
                                SelectBox.Width = item.Size.Width;
                                SelectBox.Height = item.Size.Height;
                                UpdateSelectBox();
                                UpdateToolBar();
                            }
                            else if (item.Target is DraggableResizeableControl draggable)
                            {
                                draggable._dragTransform.X = item.StartPoint.X;
                                draggable._dragTransform.Y = item.StartPoint.Y;
                                draggable.Width = item.Size.Width;
                                draggable.Height = item.Size.Height;
                            }

                            break;
                        }
                        case 截图工具.文本:
                        {
                            if (item.Target is TextCaptureTool textTool)
                            {
                                textTool.IsRedoing = true;
                                textTool.Text = (string)item.Data;
                            }
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
        _selectBytesMode = true;
        if (sender is Control { DataContext: ScreenCaptureExMethod screenCaptureExMethod })
        {
            _selectBytesModeAction = result =>
            {
                screenCaptureExMethod.Action.Invoke(result);
                result.Source?.Dispose();
            };
        }
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
