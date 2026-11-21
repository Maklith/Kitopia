using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
using MouseButton = Avalonia.Input.MouseButton;
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
    public Task Window_TitleBarAndCardBlankArea_AllowDragWithoutHijackingInteractiveControls() => RunWindowsAsync(
        (_, _, _, window, _) =>
        {
            window.ViewModel.BeginTranslation(
                "Hello world",
                TranslationSourceLanguage.Auto,
                TranslationTargetLanguage.SimplifiedChinese);
            window.ShowAt(new PixelPoint(120, 120));
            Dispatcher.UIThread.RunJobs();

            var anchorField = typeof(SelectionTranslationWindow)
                .GetField("_anchor", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var screenAnchorField = typeof(SelectionTranslationWindow)
                .GetField("_screenAnchor", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.IsNotNull(anchorField.GetValue(window));

            // Pressing inside SelectableTextBlock must keep selection interactive and preserve anchor.
            var sourceTextBlock = window.GetVisualDescendants().OfType<SelectableTextBlock>().First();
            var sourcePos = sourceTextBlock.TranslatePoint(new Point(8, 6), window)!.Value;
            window.MouseDown(sourcePos, MouseButton.Left);
            window.MouseUp(sourcePos, MouseButton.Left);
            Assert.IsNotNull(anchorField.GetValue(window));

            // Pressing blank space in the title bar must initiate drag and clear anchor.
            var card = window.FindControl<Border>("TranslationCard")!;
            var headerGrid = ((Grid)card.Child!).Children.OfType<Grid>().First();
            var blankTitlePoint = headerGrid.TranslatePoint(
                new Point(headerGrid.Bounds.Width * 0.5, headerGrid.Bounds.Height * 0.5),
                window)!.Value;
            window.MouseDown(blankTitlePoint, MouseButton.Left);
            window.MouseUp(blankTitlePoint, MouseButton.Left);
            Assert.IsNull(anchorField.GetValue(window));
            Assert.IsNull(screenAnchorField.GetValue(window));

            // Repositioning as if dragged and then receiving translated content must not snap back.
            var draggedPosition = new PixelPoint(320, 260);
            window.Position = draggedPosition;
            window.ViewModel.SetTranslatedText(string.Join('\n', Enumerable.Repeat("翻译结果", 12)));
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(draggedPosition, window.Position);
            return Task.CompletedTask;
        });

    [TestMethod]
    public Task Window_ShortAndTwoLineSourceText_DisplaysFullTextWithoutClippingOrScrolling() => RunWindowsAsync(
        (_, _, _, window, _) =>
        {
            var cases = new[]
            {
                ("Hello world", "你好，世界"),
                (string.Join('\n', Enumerable.Repeat("Long source paragraph for reuse check.", 15)), string.Join('\n', Enumerable.Repeat("长译文复用测试。", 20))),
                ("This is a slightly longer English sentence that wraps onto two lines cleanly.", "这是一句简短中文。"),
                ("Two lines of source text here that should never be clipped to one and a half lines.", string.Join('\n', Enumerable.Repeat("多行译文内容", 6))),
                ("First line\nSecond line", "译文")
            };

            foreach (var (source, translated) in cases)
            {
                window.ViewModel.BeginTranslation(
                    source,
                    TranslationSourceLanguage.Auto,
                    TranslationTargetLanguage.SimplifiedChinese);
                window.ShowAt(new PixelPoint(120, 120));
                Dispatcher.UIThread.RunJobs();

                window.ViewModel.SetTranslatedText(translated);
                Dispatcher.UIThread.RunJobs();

                var viewers = window.GetVisualDescendants().OfType<ScrollViewer>()
                    .Where(viewer => viewer.Content is SelectableTextBlock text && text.Classes.Contains("body"))
                    .ToArray();
                Assert.HasCount(2, viewers);

                var sourceViewer = viewers[0];
                var sourceTextBlock = (SelectableTextBlock)sourceViewer.Content!;
                Assert.IsTrue(sourceTextBlock.IsEffectivelyVisible, $"Source text is not visible: '{source}'");
                Assert.AreEqual(source, sourceTextBlock.Text);
                Assert.IsTrue(sourceTextBlock.Bounds.Width > 0 && sourceTextBlock.Bounds.Height > 0,
                    $"Source text has no rendered bounds: '{source}', bounds={sourceTextBlock.Bounds}");
                if (!source.Contains("Long source paragraph", StringComparison.Ordinal))
                {
                    Assert.IsTrue(sourceViewer.Viewport.Height + 0.5 >= sourceViewer.Extent.Height,
                        $"Source ScrollViewer clipped '{source}': Viewport={sourceViewer.Viewport.Height}, Extent={sourceViewer.Extent.Height}");
                    Assert.IsTrue(sourceViewer.Bounds.Height + 0.5 >= sourceTextBlock.Bounds.Height,
                        $"Source ScrollViewer bounds smaller than text '{source}': Viewer={sourceViewer.Bounds.Height}, Text={sourceTextBlock.Bounds.Height}");
                }

                var translationViewer = viewers[1];
                var translationTextBlock = (SelectableTextBlock)translationViewer.Content!;
                Assert.IsTrue(translationTextBlock.IsEffectivelyVisible, $"Translation text is not visible: '{translated}'");
                Assert.AreEqual(translated, translationTextBlock.Text);
                Assert.IsTrue(translationTextBlock.Bounds.Width > 0 && translationTextBlock.Bounds.Height > 0,
                    $"Translation text has no rendered bounds: '{translated}', bounds={translationTextBlock.Bounds}");
                if (!translated.Contains("长译文复用测试", StringComparison.Ordinal))
                {
                    Assert.IsTrue(translationViewer.Viewport.Height + 0.5 >= translationViewer.Extent.Height,
                        $"Translation ScrollViewer clipped '{translated}': Viewport={translationViewer.Viewport.Height}, Extent={translationViewer.Extent.Height}");
                }

                window.Hide();
                Dispatcher.UIThread.RunJobs();
            }

            return Task.CompletedTask;
        });

    [TestMethod]
    [DataRow(380d)]
    [DataRow(416d)]
    public Task Window_SmallScreenLongText_KeepsActionsVisibleAndBothViewersScrollable(double availableDipHeight) => RunWindowsAsync(
        (_, _, _, window, _) =>
        {
            var screen = window.Screens.Primary!;
            var scalingProperty = typeof(Screen).GetProperty(nameof(Screen.Scaling))!;
            var workingAreaProperty = typeof(Screen).GetProperty(nameof(Screen.WorkingArea))!;
            var originalScaling = screen.Scaling;
            var originalWorkingArea = screen.WorkingArea;
            try
            {
                const double scaling = 2.0;
                scalingProperty.SetValue(screen, scaling);
                workingAreaProperty.SetValue(screen, new PixelRect(0, 0, 1366, (int)(availableDipHeight * scaling)));

                var longText = string.Join('\n', Enumerable.Repeat(
                    "A long paragraph with enough text to wrap across multiple lines on small screens.", 30));
                window.ViewModel.BeginTranslation(
                    longText,
                    TranslationSourceLanguage.Auto,
                    TranslationTargetLanguage.SimplifiedChinese);
                window.ViewModel.SetTranslatedText(longText);
                window.ShowAt(new PixelPoint(100, 100));
                Dispatcher.UIThread.RunJobs();

                var copy = window.GetVisualDescendants().OfType<Button>()
                    .Single(button => button.Command == window.ViewModel.CopyTranslationCommand);
                var actionBar = (Control)copy.Parent!;
                var actionPosition = actionBar.TranslatePoint(default, window)!.Value;
                Assert.IsTrue(actionPosition.Y + actionBar.Bounds.Height <= window.ClientSize.Height - 10,
                    $"Bottom action bar clipped at maxHeight={availableDipHeight}: bottom={actionPosition.Y + actionBar.Bounds.Height}, clientHeight={window.ClientSize.Height}");

                var viewers = window.GetVisualDescendants().OfType<ScrollViewer>()
                    .Where(viewer => viewer.Content is SelectableTextBlock text && text.Classes.Contains("body"))
                    .ToArray();
                Assert.HasCount(2, viewers);
                foreach (var viewer in viewers)
                {
                    Assert.IsTrue(viewer.Viewport.Height > 0,
                        $"Viewer viewport collapsed at maxHeight={availableDipHeight}: {viewer.Viewport.Height}");
                    Assert.IsTrue(viewer.Extent.Height > viewer.Viewport.Height,
                        $"Viewer should remain scrollable at maxHeight={availableDipHeight}");
                }
            }
            finally
            {
                scalingProperty.SetValue(screen, originalScaling);
                workingAreaProperty.SetValue(screen, originalWorkingArea);
            }

            return Task.CompletedTask;
        });

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
