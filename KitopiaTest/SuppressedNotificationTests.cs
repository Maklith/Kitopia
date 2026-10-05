using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kitopia.Desktop.Services;
using PluginCore;

namespace KitopiaTest;

[TestClass]
[DoNotParallelize]
public sealed class SuppressedNotificationTests
{
    public TestContext TestContext { get; set; } = null!;

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Kitopia.Desktop.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

    [TestMethod]
    [DataRow(0, 0, 1920, 1040, 600, 1060, 1.0, 425, 597, 350, 433)]
    [DataRow(0, 0, 1920, 1000, 504, 1030, 1.5, 242, 335, 525, 650)]
    [DataRow(0, 0, 1920, 1040, 10, 1060, 1.0, 10, 597, 350, 433)]
    [DataRow(0, 0, 1920, 1040, 1910, 1060, 1.0, 1560, 597, 350, 433)]
    [DataRow(-1920, -100, 1920, 1000, -1910, 930, 1.25, -1907, 345, 438, 542)]
    [DataRow(0, 40, 1920, 1040, 600, 20, 1.0, 425, 50, 350, 433)]
    [DataRow(0, 0, 800, 600, 400, 620, 2.0, 50, 20, 700, 560)]
    public void GetPopupBounds_TrayClick_AnchorsWithinWorkArea(
        int areaX, int areaY, int areaWidth, int areaHeight, int anchorX, int anchorY, double scaling,
        int expectedX, int expectedY, int expectedWidth, int expectedHeight)
    {
        var method = typeof(SuppressedNotificationCenterWindow).GetMethod("GetPopupBounds", BindingFlags.Static | BindingFlags.NonPublic)!;
        var placement = (PixelRect)method.Invoke(null,
            [new PixelPoint(anchorX, anchorY), new PixelRect(areaX, areaY, areaWidth, areaHeight), scaling, new Size(350, 433)])!;
        Assert.AreEqual(new PixelRect(expectedX, expectedY, expectedWidth, expectedHeight), placement);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ClearAll_Clicked_ClearsNotificationsAndCanReceiveNewOnes(bool dark)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SuppressedNotificationTests));
        await session.Dispatch(() =>
        {
            var service = new ToastService();
            try
            {
                var opened = false;
                var first = Suppress(service, new ToastRequest
                {
                    Header = "新消息", Text = "设备发来了一条消息", ClickCallback = () => opened = true
                });
                var second = Suppress(service, new ToastRequest
                {
                    Header = "文件传输完成", Text = "来自另一台设备的文件已接收"
                });
                Assert.IsTrue(service.ShowSuppressedNotificationCenter());
                var window = GetField<SuppressedNotificationCenterWindow>(service, "_notificationCenterWindow");
                window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Assert.AreEqual(WindowDecorations.None, window.WindowDecorations);
                Assert.IsFalse(window.IsTitleBarVisible);
                Assert.IsFalse(window.IsMinimizeButtonVisible);
                Assert.IsFalse(window.IsRestoreButtonVisible);
                Assert.IsFalse(window.IsCloseButtonVisible);

                var button = window.FindControl<Button>("ClearAllButton")!;
                var items = window.GetVisualDescendants().OfType<ItemsControl>().Single();
                Assert.AreEqual(2, items.ItemCount);
                Assert.IsTrue(button.IsEffectivelyEnabled);
                var header = window.GetVisualDescendants().OfType<TextBlock>()
                    .Single(item => item.Text == "未读通知");
                var buttonPosition = button.TranslatePoint(default, window)!.Value;
                var headerPosition = header.TranslatePoint(default, window)!.Value;
                Assert.IsTrue(headerPosition.X + header.Bounds.Width <= buttonPosition.X);
                Assert.IsTrue(buttonPosition.X + button.Bounds.Width <= window.Bounds.Width);
                using (var frame = window.CaptureRenderedFrame())
                {
                    Assert.IsNotNull(frame);
                    var screenshot = Path.Combine(TestContext.TestRunDirectory!, $"notification-center-{dark}.png");
                    frame.Save(screenshot);
                    TestContext.AddResultFile(screenshot);
                }

                var clickPoint = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseDown(clickPoint, MouseButton.Left);
                window.MouseUp(clickPoint, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(0, items.ItemCount);
                Assert.IsFalse(button.IsEffectivelyEnabled);
                Assert.IsFalse(service.HasUnreadSuppressedNotifications());
                Assert.IsTrue(first.IsCompletedSuccessfully);
                Assert.IsTrue(second.IsCompletedSuccessfully);
                Assert.IsFalse(opened);
                Assert.IsTrue(window.GetVisualDescendants().OfType<TextBlock>()
                    .Single(item => item.Text == "暂无未读通知").IsVisible);

                var next = Suppress(service, new ToastRequest { Header = "新消息", Text = "下一条消息" });
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(1, items.ItemCount);
                Assert.IsTrue(button.IsEffectivelyEnabled);
                Assert.IsTrue(service.HasUnreadSuppressedNotifications());
                service.ClearUnreadSuppressedNotifications();
                Assert.IsTrue(next.IsCompletedSuccessfully);
            }
            finally
            {
                service.Unregister();
            }

            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    [DataRow(false, 1)]
    [DataRow(false, 2)]
    [DataRow(true, 1)]
    [DataRow(true, 2)]
    public async Task Suppress_ServiceConstructedOnAnyThread_BlinksTrayAndClearRestoresNormalIcon(bool backgroundThread, int phaseCount)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SuppressedNotificationTests));
        await session.Dispatch(async () =>
        {
            var service = backgroundThread ? await Task.Run(() => new ToastService()) : new ToastService();
            try
            {
                var timer = GetField<DispatcherTimer>(service, "_trayBlinkTimer");
                Assert.AreSame(Dispatcher.UIThread, timer.Dispatcher, "Tray blinking must run on the UI dispatcher even when the service is created during background startup.");
                Assert.AreSame(Dispatcher.UIThread, GetField<DispatcherTimer>(service, "_fullScreenMonitorTimer").Dispatcher);
                Assert.IsTrue(AssetLoader.Exists(new Uri("avares://Kitopia.Desktop/Assets/icon_notify.ico")));
                var trayIcon = TrayIcon.GetIcons(Application.Current!)![0];
                var completion = Suppress(service, new ToastRequest { Header = "New message", Text = "Preview" });
                var notifyIcon = trayIcon.Icon;
                Assert.IsNotNull(notifyIcon);
                var ticks = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var tickCount = 0;
                WindowIcon? dimIcon = notifyIcon;
                timer.Tick += (_, _) =>
                {
                    if (++tickCount == 1)
                    {
                        dimIcon = trayIcon.Icon;
                    }
                    if (tickCount == phaseCount)
                    {
                        ticks.TrySetResult();
                    }
                };
                await ticks.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.IsNotNull(dimIcon);
                Assert.AreNotSame(notifyIcon, dimIcon);
                if (phaseCount == 2)
                {
                    Assert.AreSame(notifyIcon, trayIcon.Icon);
                }
                StringAssert.Contains(trayIcon.ToolTipText!, "(1)");

                service.ClearUnreadSuppressedNotifications();
                Assert.IsFalse(timer.IsEnabled);
                Assert.IsNotNull(trayIcon.Icon);
                Assert.AreNotSame(notifyIcon, trayIcon.Icon);
                Assert.IsTrue(trayIcon.IsVisible);
                Assert.AreEqual("Kitopia.Desktop", trayIcon.ToolTipText);
                Assert.IsTrue(completion.IsCompletedSuccessfully);
            }
            finally
            {
                service.Unregister();
            }

            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    [DataRow(16)]
    [DataRow(24)]
    [DataRow(32)]
    public async Task EmptyTrayIcon_LoadedOnWindows_IsIconWithTransparentPixels(int size)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Native tray icon validation requires Windows.");
        }

        await using var session = HeadlessUnitTestSession.StartNew(typeof(SuppressedNotificationTests));
        await session.Dispatch(() =>
        {
            using var stream = AssetLoader.Open(new Uri("avares://Kitopia.Desktop/Assets/icon_empty.ico"));
            using var icon = new System.Drawing.Icon(stream, size, size);
            using var info = new Vanara.PInvoke.User32.ICONINFO();
            Assert.IsTrue(Vanara.PInvoke.User32.GetIconInfo((Vanara.PInvoke.HICON)icon.Handle, info));
            Assert.IsTrue(info.fIcon, "The empty tray frame must be an icon, not a cursor.");
            using var bitmap = icon.ToBitmap();
            Assert.AreEqual(size, bitmap.Width);
            Assert.AreEqual(size, bitmap.Height);
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                Assert.AreEqual(0, bitmap.GetPixel(x, y).A, $"Nontransparent pixel at ({x}, {y}).");
            }
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task Suppress_CenterOpenAtCapacity_UpdatesListAndUnreadCount()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SuppressedNotificationTests));
        await session.Dispatch(() =>
        {
            var service = new ToastService();
            try
            {
                var oldest = Suppress(service, new ToastRequest { Header = "Oldest", Text = "Old preview" });
                Assert.IsTrue(service.ShowSuppressedNotificationCenter());
                for (var i = 1; i <= 20; i++)
                {
                    Suppress(service, new ToastRequest { Header = $"Message {i}", Text = $"Preview {i}" });
                }

                Dispatcher.UIThread.RunJobs();
                var window = GetField<SuppressedNotificationCenterWindow>(service, "_notificationCenterWindow");
                var items = window.GetVisualDescendants().OfType<ItemsControl>().Single();
                Assert.AreEqual(20, items.ItemCount);
                var headers = window.GetVisualDescendants().OfType<TextBlock>().Select(item => item.Text).ToArray();
                CollectionAssert.Contains(headers, "Message 20");
                CollectionAssert.DoesNotContain(headers, "Oldest");
                Assert.IsTrue(oldest.IsCompletedSuccessfully);
                StringAssert.Contains(TrayIcon.GetIcons(Application.Current!)![0].ToolTipText!, "(20)");
            }
            finally
            {
                service.Unregister();
            }

            return true;
        }, CancellationToken.None);
    }

    private static Task Suppress(ToastService service, ToastRequest request)
    {
        var method = typeof(ToastService).GetMethod("SuppressToastOnUiThread", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task)method.Invoke(service, [request])!;
    }

    private static T GetField<T>(ToastService service, string name) =>
        (T)typeof(ToastService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
}
