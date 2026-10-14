using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Kitopia.Desktop.Services;
using Kitopia.Desktop.Windows;
using SkiaSharp;
#if WINDOWS
using Vanara.InteropServices;
using Vanara.PInvoke;
#endif

namespace KitopiaTest.Services;

[TestClass]
[DoNotParallelize]
public sealed class WindowEffectsTests
{
    public TestContext TestContext { get; set; } = null!;

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Kitopia.Desktop.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

#if WINDOWS
    [TestMethod]
    public async Task PowerBroadcast_OffStandardAndHighSavings_UpdatesExistingAndNewWindows()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(WindowEffectsTests));
        await session.Dispatch(() =>
        {
            var application = (Kitopia.Desktop.App)Application.Current!;
            var callback = typeof(Kitopia.Desktop.App).GetMethod("OnWindowPowerBroadcast", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var settingGuid = (Guid)typeof(Kitopia.Desktop.App).GetField("EnergySaverSettingGuid", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var window = new Window { Classes = { "acrylic" } };
            window.Show();
            try
            {
                foreach (var status in new uint[] { 0, 1, 0, 2, 0 })
                {
                    var setting = new User32.POWERBROADCAST_SETTING
                    {
                        PowerSetting = settingGuid,
                        DataLength = sizeof(uint),
                        Data = BitConverter.GetBytes(status)
                    };
                    using var payload = SafeCoTaskMemHandle.CreateFromStructure(setting);
                    object[] args = [IntPtr.Zero, (uint)User32.WindowMessage.WM_POWERBROADCAST,
                        (IntPtr)User32.PowerBroadcastType.PBT_POWERSETTINGCHANGE, payload.DangerousGetHandle(), false];
                    callback.Invoke(application, args);
                    Assert.IsFalse((bool)args[4], "Avalonia must still receive power messages.");
                    Dispatcher.UIThread.RunJobs();
                    Assert.AreEqual(status == 0 ? 0.55 : 1.0, window.Background!.Opacity);
                    Assert.AreEqual(status == 0 ? WindowTransparencyLevel.AcrylicBlur : WindowTransparencyLevel.None,
                        window.TransparencyLevelHint.Single());

                    var reopened = new Window { Classes = { "acrylic" } };
                    reopened.Show();
                    try
                    {
                        Assert.AreEqual(window.Background.Opacity, reopened.Background!.Opacity);
                        Assert.AreEqual(window.TransparencyLevelHint.Single(), reopened.TransparencyLevelHint.Single());
                    }
                    finally
                    {
                        reopened.Close();
                    }

                    setting.PowerSetting = Guid.Empty;
                    setting.Data = BitConverter.GetBytes(status == 0 ? 1u : 0u);
                    using var unrelatedPayload = SafeCoTaskMemHandle.CreateFromStructure(setting);
                    args[3] = unrelatedPayload.DangerousGetHandle();
                    callback.Invoke(application, args);
                    Assert.AreEqual(status == 0 ? 0.55 : 1.0, window.Background.Opacity,
                        "Other power settings must not affect window backgrounds.");
                }
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }
#endif

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task WindowEffects_StartupAndRuntimeChanges_UseOpaqueThemeBackgroundWhenDisabled(bool dark, bool initiallyEnabled)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(WindowEffectsTests));
        await session.Dispatch(() =>
        {
            var application = (Kitopia.Desktop.App)Application.Current!;
            application.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var updateEffects = typeof(Kitopia.Desktop.App).GetMethod("SetWindowEffectsEnabled", BindingFlags.Instance | BindingFlags.NonPublic)!;
            updateEffects.Invoke(application, [initiallyEnabled]);
            var main = new MainWindow { Width = 900, Height = 600, WindowState = WindowState.Normal };
            main.Opened -= typeof(MainWindow).GetMethod("FirstOpenEventHandler", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<EventHandler>(main);
            main.Closing -= typeof(MainWindow).GetMethod("Window_OnClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<EventHandler<WindowClosingEventArgs>>(main);
            var notification = new SuppressedNotificationCenterWindow();
            try
            {
                main.Show();
                notification.Show();
                foreach (var enabled in new[] { initiallyEnabled, false, true, false })
                {
                    updateEffects.Invoke(application, [enabled]);
                    Dispatcher.UIThread.RunJobs();
                    Assert.IsTrue(application.TryGetResource("SemiBackground0Color", application.ActualThemeVariant, out var resource));
                    var expectedColor = (Color)resource!;
                    foreach (var window in new Window[] { main, notification })
                    {
                        var background = window.Background as ISolidColorBrush;
                        Assert.IsNotNull(background);
                        Assert.AreEqual(expectedColor, background.Color);
                        Assert.AreEqual(enabled ? 0.55 : 1.0, background.Opacity);
                        Assert.AreEqual(enabled ? WindowTransparencyLevel.AcrylicBlur : WindowTransparencyLevel.None,
                            window.TransparencyLevelHint.Single());
                        var fallback = window.TransparencyBackgroundFallback as ISolidColorBrush;
                        Assert.IsNotNull(fallback);
                        Assert.AreEqual(expectedColor, fallback.Color);
                        Assert.AreEqual(1.0, fallback.Opacity);
                    }
                }

                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = main.CaptureRenderedFrame();
                Assert.IsNotNull(frame);
                var screenshot = Path.Combine(TestContext.TestRunDirectory!, $"window-effects-{dark}-{initiallyEnabled}.png");
                frame.Save(screenshot);
                TestContext.AddResultFile(screenshot);
                using var bitmap = SKBitmap.Decode(screenshot);
                var pixel = bitmap.GetPixel(2, 100);
                var color = ((ISolidColorBrush)main.Background!).Color;
                Assert.AreEqual(new SKColor(color.R, color.G, color.B, 255), pixel,
                    "The sidebar background must be fully opaque so a black compositor backdrop cannot darken it.");

                application.RequestedThemeVariant = dark ? ThemeVariant.Light : ThemeVariant.Dark;
                Dispatcher.UIThread.RunJobs();
                Assert.IsTrue(application.TryGetResource("SemiBackground0Color", application.ActualThemeVariant, out var changedResource));
                var changedColor = (Color)changedResource!;
                Assert.AreNotEqual(color, changedColor);
                Assert.AreEqual(changedColor, ((ISolidColorBrush)main.Background!).Color);
                Assert.AreEqual(1.0, main.Background!.Opacity);
            }
            finally
            {
                notification.ClosePermanently();
                main.Close();
            }
            return true;
        }, CancellationToken.None);
    }
}
