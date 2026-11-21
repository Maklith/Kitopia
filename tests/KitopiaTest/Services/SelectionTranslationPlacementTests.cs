using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Kitopia.Desktop.Abstractions.TextSelection;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.HotKey;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Translation;
using Kitopia.Desktop.Services;
using Kitopia.Desktop.ViewModels;
using Kitopia.Desktop.Windows;
using PluginCore;
using PluginCore.Config;
using SharpHook;
using SharpHook.Data;
#if WINDOWS
using Kitopia.Desktop.Platform.Windows;
using Vanara.PInvoke;
using HookMouseButton = SharpHook.Data.MouseButton;
using WindowsRect = System.Windows.Rect;
#endif

namespace KitopiaTest.Services;

[TestClass]
[DoNotParallelize]
public sealed class SelectionTranslationPlacementTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Kitopia.Desktop.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

    [TestMethod]
    public void ResolvePlacementTarget_DragOvershootsToAdjacentScreenWithSelectionBounds_StaysOnSelectionScreen()
    {
        PixelRect[] screens =
        [
            new(0, 0, 1920, 1080),
            new(1920, 0, 1920, 1080)
        ];

        var result = SelectionTranslationWindow.ResolvePlacementTarget(
            screens,
            releasePosition: new PixelPoint(1950, 505),
            selectionBounds: new TextSelectionBounds(1840, 490, 75, 20),
            sourceWindowBounds: new PixelRect(0, 0, 1920, 1080),
            pressPosition: new PixelPoint(1845, 500));

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Value.ScreenIndex);
        Assert.IsTrue(screens[0].Contains(result.Value.Anchor));
        Assert.AreEqual(new PixelPoint(1915, 510), result.Value.Anchor);
    }

    [TestMethod]
    public void ResolvePlacementTarget_DragOvershootsWithoutSelectionBounds_ClampsToSourceWindowScreen()
    {
        PixelRect[] screens =
        [
            new(0, 0, 1920, 1080),
            new(1920, 0, 1920, 1080)
        ];

        var result = SelectionTranslationWindow.ResolvePlacementTarget(
            screens,
            releasePosition: new PixelPoint(1950, 505),
            selectionBounds: null,
            sourceWindowBounds: new PixelRect(100, 100, 1820, 900),
            pressPosition: new PixelPoint(1845, 500));

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Value.ScreenIndex);
        Assert.AreEqual(new PixelPoint(1919, 505), result.Value.Anchor);
    }

    [TestMethod]
    public void ResolvePlacementTarget_ManualTriggerCursorOnOtherScreen_UsesSelectionScreen()
    {
        PixelRect[] screens =
        [
            new(0, 0, 1920, 1080),
            new(1920, 0, 1920, 1080)
        ];

        var result = SelectionTranslationWindow.ResolvePlacementTarget(
            screens,
            releasePosition: new PixelPoint(2400, 400),
            selectionBounds: new TextSelectionBounds(300, 200, 180, 24),
            sourceWindowBounds: new PixelRect(50, 50, 1200, 800),
            pressPosition: null);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Value.ScreenIndex);
        Assert.AreEqual(new PixelPoint(480, 224), result.Value.Anchor);
    }

    [TestMethod]
    public void ResolvePlacementTarget_InvalidOffscreenSelectionBounds_FallsBackToSourceWindowBounds()
    {
        PixelRect[] screens =
        [
            new(0, 0, 1920, 1080),
            new(1920, 0, 1920, 1080)
        ];

        var result = SelectionTranslationWindow.ResolvePlacementTarget(
            screens,
            releasePosition: new PixelPoint(2400, 400),
            selectionBounds: new TextSelectionBounds(2300, 300, 120, 20),
            sourceWindowBounds: new PixelRect(100, 100, 1200, 800),
            pressPosition: null);

        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Value.ScreenIndex);
        Assert.IsTrue(screens[0].Contains(result.Value.Anchor));
        Assert.AreEqual(new PixelPoint(1299, 400), result.Value.Anchor);
    }

    [TestMethod]
    [DataRow(0, 0, 1920, 1040, 1910, 500, 1.0, 1.0, 440, 240)]
    [DataRow(-1920, 0, 1920, 1040, -10, 500, 1.0, 1.0, 440, 240)]
    [DataRow(1920, 0, 1920, 1040, 3830, 1030, 1.0, 1.0, 440, 240)]
    [DataRow(1920, 0, 1920, 1040, 3830, 500, 1.0, 2.0, 440, 240)]
    [DataRow(0, 0, 500, 300, 495, 295, 2.0, 2.0, 440, 240)]
    public void CalculateWindowPosition_ScreenEdgesAndMixedDpi_StaysInsideWorkingArea(
        int areaX, int areaY, int areaWidth, int areaHeight,
        int anchorX, int anchorY,
        double targetScaling, double currentScaling,
        double dipWidth, double dipHeight)
    {
        var workingArea = new PixelRect(areaX, areaY, areaWidth, areaHeight);
        var pos = SelectionTranslationWindow.CalculateWindowPosition(
            new PixelPoint(anchorX, anchorY),
            workingArea,
            targetScaling,
            new Size(dipWidth, dipHeight),
            currentScaling);

        var effectiveScaling = Math.Max(targetScaling, currentScaling);
        var width = Math.Clamp((int)Math.Ceiling(dipWidth * effectiveScaling), 1, workingArea.Width);
        var height = Math.Clamp((int)Math.Ceiling(dipHeight * effectiveScaling), 1, workingArea.Height);
        var windowRect = new PixelRect(pos, new PixelSize(width, height));

        Assert.IsTrue(workingArea.Contains(windowRect),
            $"Window {windowRect} must remain strictly inside workingArea {workingArea}.");
    }

#if WINDOWS
    [TestMethod]
    public void ComputeUnionBounds_MultiLineAndEmptyRectangles_ReturnsUnionBounds()
    {
        WindowsRect[] rectangles =
        [
            WindowsRect.Empty,
            new(100, 200, 0, 18),
            new(120, 210, 300, 20),
            new(90, 232, 360, 20)
        ];

        var union = WindowsTextSelectionService.ComputeUnionBounds(rectangles);

        Assert.IsNotNull(union);
        Assert.AreEqual(new TextSelectionBounds(90, 210, 360, 42), union.Value);
    }

    [TestMethod]
    public Task TriggerAutomatic_SameLanguageOrNonNaturalText_SkipsWithoutShowingWindow() => RunWindowsAsync(
        async (hotkeys, hook, backend, window, service) =>
        {
            backend.Text = "客户端使用 Avalonia 实现自动划词翻译";
            await service.TriggerAutomaticAsync(ConfigManger.Config.selectionTranslationAutoHotKey)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(1, backend.SelectionReadCount);
            Assert.IsFalse(window.IsVisible);
            Assert.AreEqual(0, backend.TranslationCount);

            backend.Text = "https://github.com/liaom/Kitopia";
            await service.TriggerAutomaticAsync(ConfigManger.Config.selectionTranslationAutoHotKey)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(2, backend.SelectionReadCount);
            Assert.IsFalse(window.IsVisible);
            Assert.AreEqual(0, backend.TranslationCount);

            ConfigManger.Config.language = "en-US";
            backend.Text = "Hello world";
            await service.TriggerAutomaticAsync(ConfigManger.Config.selectionTranslationAutoHotKey)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(3, backend.SelectionReadCount);
            Assert.IsFalse(window.IsVisible);
            Assert.AreEqual(0, backend.TranslationCount);
        });

    [TestMethod]
    public Task TriggerAutomatic_ForegroundWindowMovedOrResizedDuringDrag_SkipsWithoutShowingWindow() => RunWindowsAsync(
        async (_, _, backend, window, service) =>
        {
            var desktop = (nint)User32.GetDesktopWindow();
            Assert.AreNotEqual((nint)0, desktop);

            var tryGetBoundsMethod = typeof(SelectionTranslationService)
                .GetMethod("TryGetWindowBounds", BindingFlags.Static | BindingFlags.NonPublic)!;
            var args = new object?[] { desktop, null };
            Assert.IsTrue((bool)tryGetBoundsMethod.Invoke(null, args)!);
            var actualBounds = (PixelRect)args[1]!;

            var mousePressType = typeof(SelectionTranslationService)
                .GetNestedType("MousePressState", BindingFlags.NonPublic)!;
            var staleBounds = new PixelRect(
                actualBounds.X, actualBounds.Y, actualBounds.Width + 50, actualBounds.Height);
            var pressState = Activator.CreateInstance(
                mousePressType, desktop, staleBounds, new PixelPoint(actualBounds.X + 10, actualBounds.Y + 10))!;
            typeof(SelectionTranslationService)
                .GetField("_lastMousePress", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(service, pressState);

            service.GetForegroundWindow = () => desktop;
            backend.SourceWindow = desktop;
            backend.Text = "Hello world";

            await service.TriggerAutomaticAsync(ConfigManger.Config.selectionTranslationAutoHotKey)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();

            Assert.IsFalse(window.IsVisible);
            Assert.AreEqual(0, backend.SelectionReadCount);
            Assert.AreEqual(0, backend.TranslationCount);
        });

    [TestMethod]
    public Task TriggerAutomatic_OversizedText_SkipsSilentlyWhileManualShowsError() => RunWindowsAsync(
        async (_, _, backend, window, service) =>
        {
            backend.Text = new string('a', 1001);
            await service.TriggerAutomaticAsync(ConfigManger.Config.selectionTranslationAutoHotKey)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.IsFalse(window.IsVisible);
            Assert.IsFalse(window.ViewModel.HasError);
            Assert.AreEqual(0, backend.TranslationCount);

            await service.TriggerManualAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(window.IsVisible);
            Assert.IsTrue(window.ViewModel.HasError);
            Assert.AreEqual(0, backend.TranslationCount);
        });

    [TestMethod]
    public Task TriggerManual_SameLanguageAndUrl_StillTranslates() => RunWindowsAsync(
        async (_, _, backend, window, service) =>
        {
            backend.Text = "你好世界";
            await service.TriggerManualAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(window.IsVisible);
            Assert.AreEqual(1, backend.TranslationCount);

            backend.Text = "https://example.com/docs";
            await service.TriggerManualAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(window.IsVisible);
            Assert.AreEqual(2, backend.TranslationCount);
        });

    [TestMethod]
    [DataRow("key")]
    [DataRow("wheel")]
    [DataRow("right-click")]
    public Task TriggerAutomatic_InterruptedDuringDelay_CancelsPendingTrigger(string inputKind) => RunWindowsAsync(
        async (_, _, backend, window, service) =>
        {
            backend.Text = "Hello world";
            var pendingField = typeof(SelectionTranslationService)
                .GetField("_pendingAutomaticCancellation", BindingFlags.Instance | BindingFlags.NonPublic)!;

            var triggerTask = service.TriggerAutomaticAsync(ConfigManger.Config.selectionTranslationAutoHotKey);
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (pendingField.GetValue(service) is null && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }
            Assert.IsNotNull(pendingField.GetValue(service));

            switch (inputKind)
            {
                case "key":
                    typeof(SelectionTranslationService)
                        .GetMethod("OnKeyPressed", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(service, [null, new KeyboardHookEventArgs(new UioHookEvent
                        {
                            Keyboard = new KeyboardEventData { KeyCode = KeyCode.VcC }
                        })]);
                    break;
                case "wheel":
                    typeof(SelectionTranslationService)
                        .GetMethod("OnMouseWheel", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(service, [null, new MouseWheelHookEventArgs(new UioHookEvent
                        {
                            Wheel = new MouseWheelEventData { Rotation = 1 }
                        })]);
                    break;
                case "right-click":
                    typeof(SelectionTranslationService)
                        .GetMethod("OnMousePressed", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(service, [null, new MouseHookEventArgs(new UioHookEvent
                        {
                            Mouse = new MouseEventData { Button = HookMouseButton.Button2 }
                        })]);
                    break;
            }

            await triggerTask.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.IsFalse(window.IsVisible);
            Assert.AreEqual(0, backend.SelectionReadCount);
            Assert.AreEqual(0, backend.TranslationCount);
        });

    [TestMethod]
    public Task TriggerAutomatic_DragInsideSelectionTranslationWindow_DoesNotRetrigger() => RunWindowsAsync(
        async (_, _, backend, window, service) =>
        {
            backend.Text = "Hello world";
            await service.TriggerAutomaticAsync(new HotKeyModel(ConfigManger.Config.selectionTranslationAutoHotKey)
            {
                TriggerPosition = new PixelPoint(120, 120)
            }).WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(window.IsVisible);
            Assert.AreEqual(1, backend.TranslationCount);
            Assert.AreEqual("Hello world", window.ViewModel.SourceText);

            // Release point inside the visible SelectionTranslationWindow.
            backend.Text = "Another English phrase";
            await service.TriggerAutomaticAsync(new HotKeyModel(ConfigManger.Config.selectionTranslationAutoHotKey)
            {
                TriggerPosition = new PixelPoint(window.Position.X + 24, window.Position.Y + 24)
            }).WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(1, backend.TranslationCount);
            Assert.AreEqual("Hello world", window.ViewModel.SourceText);

            // Drag started inside the visible SelectionTranslationWindow and released outside.
            typeof(SelectionTranslationService)
                .GetField("_pressedInsideWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(service, true);
            await service.TriggerAutomaticAsync(new HotKeyModel(ConfigManger.Config.selectionTranslationAutoHotKey)
            {
                TriggerPosition = new PixelPoint(10, 10)
            }).WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(1, backend.TranslationCount);
            Assert.AreEqual("Hello world", window.ViewModel.SourceText);
        });

    private static async Task RunWindowsAsync(
        Func<HotKeyImpl, SimpleGlobalHook, StubSelectionBackend, SelectionTranslationWindow, SelectionTranslationService, Task> test)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SelectionTranslationPlacementTests));
        await session.Dispatch(async () =>
        {
            var originalConfigs = ConfigManger.Configs;
            ConfigManger.Configs = new Dictionary<string, ConfigBase>
            {
                ["KitopiaConfig"] = new KitopiaConfig { language = "zh-CN" }
            };
            const nint testForegroundWindow = 0x1234;
            var hotkeys = new HotKeyImpl();
            using var hook = new SimpleGlobalHook();
            var backend = new StubSelectionBackend { SourceWindow = testForegroundWindow };
            var window = new SelectionTranslationWindow(
                new SelectionTranslationWindowViewModel(null!),
                new WindowToolServiceWindow());
            var service = new SelectionTranslationService(hook, hotkeys, backend, backend, window)
            {
                GetForegroundWindow = () => testForegroundWindow
            };
            service.Start();
            try
            {
                await test(hotkeys, hook, backend, window, service);
            }
            finally
            {
                service.Stop();
                Dispatcher.UIThread.RunJobs();
                window.Close();
                foreach (var model in hotkeys.GetAllRegistered())
                    hotkeys.Remove(model.UUID);
                ConfigManger.Configs = originalConfigs;
            }
            return true;
        }, CancellationToken.None);
    }

    private sealed class StubSelectionBackend : ITextSelectionService, ISelectionTextTranslator
    {
        public string Text { get; set; } = "Hello world";
        public nint SourceWindow { get; set; }
        public int SelectionReadCount { get; private set; }
        public int TranslationCount { get; private set; }

        public Task<TextSelectionSnapshot?> TryGetSelectionAsync(
            bool allowClipboardFallback,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SelectionReadCount++;
            return Task.FromResult<TextSelectionSnapshot?>(new TextSelectionSnapshot(
                Text,
                SourceWindow,
                "selection-test",
                null));
        }

        public Task<string> TranslateSelectionAsync(
            string text,
            TranslationSourceLanguage sourceLanguage = TranslationSourceLanguage.Auto,
            TranslationTargetLanguage targetLanguage = TranslationTargetLanguage.SimplifiedChinese,
            CancellationToken cancellationToken = default)
        {
            TranslationCount++;
            return Task.FromResult("translated: " + text);
        }
    }
#endif
}
