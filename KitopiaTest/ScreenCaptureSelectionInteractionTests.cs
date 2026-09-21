using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Controls.Capture;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Windows;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using PluginCore;
using Point = Avalonia.Point;
using Rect = Avalonia.Rect;
using Size = Avalonia.Size;

namespace KitopiaTest;

[TestClass]
[DoNotParallelize]
public sealed class ScreenCaptureSelectionInteractionTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Kitopia.Desktop.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .UseSkia();

    [TestMethod]
    [DataRow(8d, 8d)]
    [DataRow(-8d, -8d)]
    [DataRow(8d, -8d)]
    [DataRow(-8d, 8d)]
    public Task PointerPressed_ThenShortDrag_PreviewsAndSavesExactPointerBounds(double deltaX, double deltaY)
    {
        return RunSelectionTestAsync(window =>
        {
            ScreenCaptureInfo? selected = null;
            window.SetToSelectMode(info => selected = info);
            var start = new Point(100, 100);
            var end = new Point(start.X + deltaX, start.Y + deltaY);
            var selectBox = window.FindControl<DraggableResizeableControl>("SelectBox")!;

            window.MouseMove(start);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(new Rect(30, 40, 600, 500), selectBox.ContentRect);

            window.MouseDown(start, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(new Rect(start, new Size(0, 0)), selectBox.ContentRect);
            Assert.IsFalse(selectBox.IsHitTestVisible);
            Assert.IsFalse(window.FindControl<Border>("ColorInspector")!.IsVisible);
            Assert.IsNull(selected);

            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            var expected = new Rect(start, end).Normalize();
            Assert.AreEqual(expected, selectBox.ContentRect);
            Assert.IsNull(selected);

            window.MouseUp(end, MouseButton.Left);
            Assert.IsTrue(selected.HasValue);
            Assert.AreEqual(ScreenCaptureType.屏幕, selected.Value.ScreenCaptureType);
            Assert.AreEqual(new PluginCore.Rect((int)expected.X, (int)expected.Y, 8, 8),
                selected.Value.RequestRect);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    [DataRow(false, 0d)]
    [DataRow(true, 0d)]
    [DataRow(true, 1d)]
    public Task PointerPressed_ThenClick_SelectsWindow(bool hoverFirst, double jitter)
    {
        return RunSelectionTestAsync(window =>
        {
            ScreenCaptureInfo? selected = null;
            window.SetToSelectMode(info => selected = info);
            var start = new Point(100, 100);
            var end = new Point(start.X + jitter, start.Y + jitter);
            if (hoverFirst)
            {
                window.MouseMove(start);
                Dispatcher.UIThread.RunJobs();
            }

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            Assert.IsNull(selected);
            window.MouseUp(end, MouseButton.Left);

            Assert.IsTrue(selected.HasValue);
            Assert.AreEqual(ScreenCaptureType.窗口, selected.Value.ScreenCaptureType);
            Assert.AreEqual(new PluginCore.Rect(30, 40, 600, 500), selected.Value.RequestRect);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public Task PointerCaptureLost_DuringDrag_CancelsWithoutSavingAndAllowsNewSelection()
    {
        return RunSelectionTestAsync(window =>
        {
            ScreenCaptureInfo? selected = null;
            IPointer? pointer = null;
            window.SetToSelectMode(info => selected = info);
            window.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
                RoutingStrategies.Tunnel);
            var start = new Point(100, 100);
            var end = new Point(120, 110);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            Assert.IsNotNull(pointer);
            pointer.Capture(null);

            Assert.IsNull(selected);
            Assert.IsFalse(window.FindControl<DraggableResizeableControl>("SelectBox")!.IsHitTestVisible);
            window.MouseUp(end, MouseButton.Left);
            Assert.IsNull(selected);

            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);
            Assert.AreEqual(new PluginCore.Rect(100, 100, 20, 10), selected?.RequestRect);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public Task SelectedRegion_DragAndResize_KeepsPointerAlignedWithContent()
    {
        return RunSelectionTestAsync(window =>
        {
            window.MouseDown(new Point(100, 100), MouseButton.Left);
            window.MouseMove(new Point(300, 200), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(300, 200), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            var selectBox = window.FindControl<DraggableResizeableControl>("SelectBox")!;

            window.MouseDown(new Point(150, 150), MouseButton.Left);
            window.MouseMove(new Point(170, 160), RawInputModifiers.LeftMouseButton);
            window.MouseMove(new Point(180, 170), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(180, 170), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(new Rect(130, 120, 200, 100), selectBox.ContentRect);

            var thumb = selectBox.GetVisualDescendants().OfType<Thumb>().Single(control => control.Name == "ThumbTL");
            var thumbCenter = thumb.TranslatePoint(new Point(4, 4), window)!.Value;
            Assert.AreEqual(selectBox.ContentRect.Position, thumbCenter);
            window.MouseDown(thumbCenter, MouseButton.Left);
            window.MouseMove(thumbCenter + new Vector(20, 10), RawInputModifiers.LeftMouseButton);
            window.MouseMove(thumbCenter + new Vector(30, 20), RawInputModifiers.LeftMouseButton);
            window.MouseUp(thumbCenter + new Vector(30, 20), MouseButton.Left);
            Assert.AreEqual(new Rect(160, 140, 170, 80), selectBox.ContentRect);

            ScreenCaptureInfo? selected = null;
            window.SetToSelectMode(info => selected = info);
            window.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(ToolTip.GetTip(button), "复制到剪贴板"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.AreEqual(new PluginCore.Rect(160, 140, 170, 80), selected?.RequestRect);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    [DataRow(1d)]
    [DataRow(1.25d)]
    [DataRow(1.5d)]
    [DataRow(2d)]
    public Task FinishCapture_ScaledDisplay_PreservesLeftEdgePixels(double scaling)
    {
        return RunSelectionTestAsync(async window =>
        {
            window.SetRenderScaling(scaling);
            window.Width = 800 / scaling;
            window.Height = 600 / scaling;
            Dispatcher.UIThread.RunJobs();
            var completion = new TaskCompletionSource<ScreenCaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            window.SetToSelectBytesMode(result => completion.SetResult(result), () => completion.TrySetCanceled());
            var start = new Point(100 / scaling, 100 / scaling);
            var end = new Point(400 / scaling, 300 / scaling);
            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);

            var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var output = result.Source;
            Assert.IsNotNull(output);
            Assert.AreEqual(300, output.Width);
            Assert.AreEqual(200, output.Height);
            foreach (var y in new[] { 0, 1, 99, 100, 101, 199 })
            foreach (var x in new[] { 0, 1, 20, 99, 100, 101, 150, 299 })
            {
                var pixel = output.At<Vec4b>(y, x);
                var expected = x < 100 ? new Vec4b(17, 83, 129, 255) : new Vec4b(211, 47, 31, 255);
                if (y >= 100)
                    expected = new Vec4b(11, 219, 71, 255);
                Assert.AreEqual(expected, pixel, $"Unexpected pixel at ({x},{y}), scaling={scaling}.");
            }
        });
    }

    [TestMethod]
    [DataRow(1d)]
    [DataRow(1.25d)]
    [DataRow(1.5d)]
    [DataRow(2d)]
    public Task FinishCapture_AfterLayout_PreservesPixelsAndAnnotations(double scaling)
    {
        return RunSelectionTestAsync(async window =>
        {
            window.SetRenderScaling(scaling);
            window.Width = 800 / scaling;
            window.Height = 600 / scaling;
            ConfigManger.Config.截图直接复制到剪贴板 = false;
            Dispatcher.UIThread.RunJobs();
            var start = new Point(100 / scaling, 100 / scaling);
            var end = new Point(462 / scaling, 376 / scaling);
            window.MouseMove(start);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);
            var annotation = new DraggableResizeableControl
            {
                ContentRect = new Rect(120 / scaling, 120 / scaling, 40 / scaling, 40 / scaling),
                IsSelected = true,
                Content = new Avalonia.Controls.Shapes.Rectangle { Fill = Avalonia.Media.Brushes.Red }
            };
            window.FindControl<Canvas>("Canvas")!.Children.Add(annotation);
            Dispatcher.UIThread.RunJobs();

            var completion = new TaskCompletionSource<ScreenCaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            window.SetToSelectBytesMode(result => completion.SetResult(result), () => completion.TrySetCanceled());
            var saveButton = window.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(ToolTip.GetTip(button), "复制到剪贴板"));
            saveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var output = result.Source;
            Assert.IsNotNull(output);
            Assert.AreEqual(362, output.Width);
            Assert.AreEqual(276, output.Height);
            foreach (var y in new[] { 0, 1, 99, 100, 101, 275 })
            foreach (var x in new[] { 0, 1, 99, 100, 101, 361 })
            {
                var expected = y >= 100 ? new Vec4b(11, 219, 71, 255)
                    : x < 100 ? new Vec4b(17, 83, 129, 255) : new Vec4b(211, 47, 31, 255);
                Assert.AreEqual(expected, output.At<Vec4b>(y, x),
                    $"Unexpected pixel at ({x},{y}), scaling={scaling}.");
            }
            Assert.AreEqual(new Vec4b(0, 0, 255, 255), output.At<Vec4b>(40, 40));
        });
    }

    private static async Task RunSelectionTestAsync(Func<ScreenCaptureWindow, Task> test)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ScreenCaptureSelectionInteractionTests));
        await session.Dispatch(async () =>
        {
            var previousServices = ServiceManager.Services;
            ConfigManger.Configs.TryGetValue("KitopiaConfig", out var previousConfig);
            using var services = new ServiceCollection()
                .AddSingleton<IScreenCaptureManager, TestCaptureManager>()
                .BuildServiceProvider();
            using var image = new Mat(600, 800, MatType.CV_8UC4, new Scalar(17, 83, 129, 255));
            using (var rightBand = image[new OpenCvSharp.Rect(200, 0, 600, 600)])
                rightBand.SetTo(new Scalar(211, 47, 31, 255));
            using (var bottomBand = image[new OpenCvSharp.Rect(0, 200, 800, 400)])
                bottomBand.SetTo(new Scalar(11, 219, 71, 255));
            ScreenCaptureWindow? window = null;
            try
            {
                ServiceManager.Services = services;
                ConfigManger.Configs["KitopiaConfig"] = new KitopiaConfig();
                window = new ScreenCaptureWindow(new[]
                {
                    new ScreenCaptureResult
                    {
                        Source = image,
                        Info = new ScreenCaptureInfo { ScreenInfo = new PluginCore.Rect(0, 0, 800, 600) }
                    }
                });
                window.Show();
                Dispatcher.UIThread.RunJobs();
                await test(window);
            }
            finally
            {
                window?.Close();
                if (window != null)
                    WeakReferenceMessenger.Default.UnregisterAll(window);
                ServiceManager.Services = previousServices;
                if (previousConfig == null)
                    ConfigManger.Configs.Remove("KitopiaConfig");
                else
                    ConfigManger.Configs["KitopiaConfig"] = previousConfig;
            }
            return true;
        }, CancellationToken.None);
    }

    private sealed class TestCaptureManager : IScreenCaptureManager
    {
        public List<WindowInfo> GetAllWindowInfo() =>
        [
            new WindowInfo { Hwnd = (IntPtr)1, Rect = new PluginCore.Rect(30, 40, 600, 500), ZIndex = 0 }
        ];

        public void SetCaptureMethodName(string methodName) => throw new NotSupportedException();
        public List<string> GetCaptureMethodName() => throw new NotSupportedException();
        public List<ScreenCaptureInfo> GetAllScreenInfo() => throw new NotSupportedException();
        public ScreenCaptureInfo GetScreenCaptureInfoByIndex(int index) => throw new NotSupportedException();
        public Stack<ScreenCaptureResult> CaptureAllScreenBytes() => throw new NotSupportedException();
        public ScreenCaptureResult CaptureScreenBytes(ScreenCaptureInfo info) => throw new NotSupportedException();
    }
}
