using System.Reflection;
using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kitopia.Desktop.Controls;
using Kitopia.Desktop.Services;
using Kitopia.Feature.Localization;
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
                var items = window.FindControl<ItemsControl>("NotificationItemsControl")!;
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
                var items = window.FindControl<ItemsControl>("NotificationItemsControl")!;
                Assert.AreEqual(20, items.ItemCount);
                var headers = window.GetVisualDescendants().OfType<TextBlock>().Select(item => item.Text).ToArray();
                CollectionAssert.Contains(headers, "Message 20");
                CollectionAssert.DoesNotContain(headers, "Oldest");
                Assert.IsTrue(oldest.IsCompletedSuccessfully);
                Assert.AreEqual(20, GetField<IDictionary>(service, "_items").Count);
                StringAssert.Contains(TrayIcon.GetIcons(Application.Current!)![0].ToolTipText!, "(20)");
            }
            finally
            {
                service.Unregister();
            }

            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    [DataRow(NotificationType.Information, false, "zh-CN", 350)]
    [DataRow(NotificationType.Success, true, "zh-CN", 280)]
    [DataRow(NotificationType.Warning, false, "en-US", 280)]
    [DataRow(NotificationType.Error, true, "en-US", 350)]
    public async Task SuppressedCard_NotificationTypeProgressAndActions_MatchNormalToast(
        NotificationType type, bool dark, string language, int width)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SuppressedNotificationTests));
        await session.Dispatch(async () =>
        {
            var originalLanguage = Lang.Current.Language;
            Lang.Current.UseLanguage(language);
            Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var normal = new ToastService();
            var suppressed = new ToastService();
            try
            {
                var request = new ToastRequest
                {
                    Header = "Phone", Text = "project-archive-with-a-very-long-file-name.zip (10 MB)",
                    NotificationType = type, AutoCloseDelay = null,
                    ShowProgressBar = true, ProgressValue = 45,
                    Actions =
                    [
                        new ToastAction { Text = Lang.Get("lang.kitopia.agree"), IsPrimary = true },
                        new ToastAction { Text = Lang.Get("lang.kitopia.reject") },
                        new ToastAction { Text = Lang.Get("lang.kitopia.open_chat") }
                    ]
                };
                var add = typeof(ToastService).GetMethod("AddToastOnUiThread", BindingFlags.Instance | BindingFlags.NonPublic)!;
                add.Invoke(normal, [request, false]);
                Suppress(suppressed, request);
                Assert.IsTrue(suppressed.ShowSuppressedNotificationCenter());
                var normalWindow = GetField<ToastShowWindow>(normal, "_toastShowWindow");
                var centerWindow = GetField<SuppressedNotificationCenterWindow>(suppressed, "_notificationCenterWindow");
                centerWindow.Width = width;
                await Task.Delay(350);
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var normalCard = normalWindow.GetVisualDescendants().OfType<ToastCard>().Single();
                var centerCard = centerWindow.GetVisualDescendants().OfType<ToastCard>().Single();
                Assert.AreEqual(normalCard.DataContext!.GetType(), centerCard.DataContext!.GetType());
                var createdAtText = (string)centerCard.DataContext.GetType().GetProperty("CreatedAtText")!.GetValue(centerCard.DataContext)!;
                Assert.IsTrue(centerWindow.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == createdAtText));
                foreach (var card in new[] { normalCard, centerCard })
                {
                    CollectionAssert.Contains(card.Classes.ToArray(), type.ToString().ToLowerInvariant());
                    var icon = card.FindControl<PathIcon>("ToastIcon")!;
                    Assert.AreSame(card.FindResource($"NotificationCard{type}IconPathData"), icon.Data);
                    Assert.AreEqual(45d, card.GetVisualDescendants().OfType<ProgressBar>().Single().Value);
                    var buttons = card.GetVisualDescendants().OfType<Button>()
                        .Where(button => button.Classes.Contains("toast-action")).ToArray();
                    Assert.HasCount(3, buttons);
                    CollectionAssert.AreEqual(request.Actions.Select(action => action.Text).ToArray(), buttons.Select(button => button.Content).ToArray());
                    Assert.IsTrue(buttons[0].Classes.Contains("primary"));
                    Assert.AreEqual(new CornerRadius(8), card.FindControl<Border>("ToastCardBorder")!.CornerRadius);
                    foreach (var control in card.GetVisualDescendants().OfType<Control>()
                                 .Where(control => control.IsEffectivelyVisible && control is Button or TextBlock or ProgressBar))
                    {
                        var point = control.TranslatePoint(default, card)!.Value;
                        Assert.IsTrue(point.X >= -0.5 && point.X + control.Bounds.Width <= card.Bounds.Width + 0.5,
                            $"{control.GetType().Name} exceeds card width at {width}.");
                    }
                }
                Assert.AreEqual(normalCard.FindControl<PathIcon>("ToastIcon")!.Foreground, centerCard.FindControl<PathIcon>("ToastIcon")!.Foreground);
                var screenshotDirectory = Path.Combine(AppContext.BaseDirectory, "TestResults", "toast-display");
                Directory.CreateDirectory(screenshotDirectory);
                using var normalFrame = normalWindow.CaptureRenderedFrame();
                using var centerFrame = centerWindow.CaptureRenderedFrame();
                Assert.IsNotNull(normalFrame);
                Assert.IsNotNull(centerFrame);
                var normalScreenshot = Path.Combine(screenshotDirectory, $"normal-{type}-{language}-{dark}.png");
                var centerScreenshot = Path.Combine(screenshotDirectory, $"suppressed-{type}-{language}-{dark}.png");
                normalFrame.Save(normalScreenshot);
                centerFrame.Save(centerScreenshot);
                TestContext.AddResultFile(normalScreenshot);
                TestContext.AddResultFile(centerScreenshot);
            }
            finally
            {
                normal.Unregister();
                suppressed.Unregister();
                Lang.Current.UseLanguage(originalLanguage);
            }
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    [DataRow("Accept", false)]
    [DataRow("Accept", true)]
    [DataRow("Reject", false)]
    [DataRow("Reject", true)]
    public async Task SuppressedOffer_ActionClick_InvokesOnlyActionAndRespectsCloseSetting(string actionText, bool close)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SuppressedNotificationTests));
        await session.Dispatch(() =>
        {
            var service = new ToastService();
            var clicked = 0;
            var accepted = 0;
            var rejected = 0;
            try
            {
                var completion = Suppress(service, new ToastRequest
                {
                    Header = "Phone", Text = "archive.zip", ClickCallback = () => clicked++,
                    Actions =
                    [
                        new ToastAction { Text = "Accept", Callback = () => accepted++, CloseOnClick = close, IsPrimary = true },
                        new ToastAction { Text = "Reject", Callback = () => rejected++, CloseOnClick = close }
                    ]
                });
                Assert.IsTrue(service.ShowSuppressedNotificationCenter());
                var window = GetField<SuppressedNotificationCenterWindow>(service, "_notificationCenterWindow");
                Dispatcher.UIThread.RunJobs();
                var button = window.GetVisualDescendants().OfType<Button>().Single(value => Equals(value.Content, actionText));
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                Assert.AreEqual(actionText == "Accept" ? 1 : 0, accepted);
                Assert.AreEqual(actionText == "Reject" ? 1 : 0, rejected);
                Assert.AreEqual(0, clicked);
                Assert.AreEqual(close, completion.IsCompletedSuccessfully);
                Assert.AreEqual(!close, service.HasUnreadSuppressedNotifications());
            }
            finally
            {
                service.Unregister();
            }
        }, CancellationToken.None);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SuppressedProgress_UpdatedAndFinished_PreservesLiveStateUntilClosed(bool failed)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SuppressedNotificationTests));
        await session.Dispatch(async () =>
        {
            var service = new ToastService();
            try
            {
                var completion = Suppress(service, new ToastRequest
                {
                    Header = "Phone", Text = "archive.zip", ShowProgressBar = true, IsProgressIndeterminate = true
                });
                var entries = GetField<IDictionary>(service, "_items");
                var id = (Guid)entries.Keys.Cast<object>().Single();
                var handleType = typeof(ToastService).GetNestedType("ToastProgressHandle", BindingFlags.NonPublic)!;
                var handle = (IToastProgressHandle)Activator.CreateInstance(handleType,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [service, id], null)!;
                Assert.IsTrue(service.ShowSuppressedNotificationCenter());
                var window = GetField<SuppressedNotificationCenterWindow>(service, "_notificationCenterWindow");
                Dispatcher.UIThread.RunJobs();
                await Task.Run(() => handle.Update(65, "Receiving 65%", "Tablet", false));
                Dispatcher.UIThread.RunJobs();
                var card = window.GetVisualDescendants().OfType<ToastCard>().Single();
                var progress = card.GetVisualDescendants().OfType<ProgressBar>().Single();
                Assert.AreEqual(65d, progress.Value);
                Assert.IsFalse(progress.IsIndeterminate);
                Assert.IsTrue(card.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Receiving 65%"));
                if (failed) handle.Fail("Transfer failed", autoCloseDelay: TimeSpan.FromMilliseconds(1));
                else handle.Complete("Saved", autoCloseDelay: TimeSpan.FromMilliseconds(1));
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(failed ? 65d : 100d, progress.Value);
                CollectionAssert.Contains(card.Classes.ToArray(), failed ? "error" : "success");
                Assert.AreSame(card.FindResource(failed ? "NotificationCardErrorIconPathData" : "NotificationCardSuccessIconPathData"),
                    card.FindControl<PathIcon>("ToastIcon")!.Data);
                Assert.AreEqual(0, GetField<IDictionary>(service, "_autoCloseCtsMap").Count);
                Assert.IsFalse(completion.IsCompleted);
                handle.Close();
                Assert.IsTrue(completion.IsCompletedSuccessfully);
                Assert.IsFalse(service.HasUnreadSuppressedNotifications());
                Assert.AreEqual(0, entries.Count);
                handle.Update(75);
                Assert.AreEqual(0, entries.Count);
            }
            finally
            {
                service.Unregister();
            }
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OpenLatest_CloseSetting_PreservesOtherNotifications(bool close)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SuppressedNotificationTests));
        await session.Dispatch(() =>
        {
            var service = new ToastService();
            var clicked = 0;
            try
            {
                var older = Suppress(service, new ToastRequest { Header = "Older", Text = "Message" });
                var latest = Suppress(service, new ToastRequest
                {
                    Header = "Latest", Text = "Message", ClickCallback = () => clicked++, CloseOnClick = close
                });
                Assert.IsTrue(service.TryOpenLatestSuppressedNotification());
                Assert.AreEqual(1, clicked);
                Assert.AreEqual(close, latest.IsCompletedSuccessfully);
                Assert.IsFalse(older.IsCompleted);
                Assert.AreEqual(close ? 1 : 2, GetField<IDictionary>(service, "_items").Count);
            }
            finally
            {
                service.Unregister();
            }
        }, CancellationToken.None);
    }

    private static Task Suppress(ToastService service, ToastRequest request)
    {
        var add = typeof(ToastService).GetMethod("AddToastOnUiThread", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var id = (Guid)add.Invoke(service, [request, true])!;
        var completion = typeof(ToastService).GetMethod("GetOrCreateDismissedTaskOnUiThread", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task)completion.Invoke(service, [id])!;
    }

    private static T GetField<T>(ToastService service, string name) =>
        (T)typeof(ToastService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
}
