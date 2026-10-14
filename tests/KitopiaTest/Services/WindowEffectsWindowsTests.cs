#if WINDOWS
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Kitopia.Desktop.Services;
using Vanara.Extensions;
using Vanara.InteropServices;
using Vanara.PInvoke;
using Windows.UI.ViewManagement;

namespace KitopiaTest.Services;

[TestClass]
[DoNotParallelize]
public sealed class WindowEffectsWindowsTests
{
    public TestContext TestContext { get; set; } = null!;

    [STATestMethod]
    [TestCategory("WindowsIntegration")]
    public void PowerNotifications_RealWindow_ReceivesInitialSystemStateAndFollowsChanges()
    {
        // Native Avalonia initialization must run in a separate process from headless tests.
        if (Environment.GetEnvironmentVariable("KITOPIA_RUN_WINDOWS_UI_TESTS") != "1")
            Assert.Inconclusive("Set KITOPIA_RUN_WINDOWS_UI_TESTS=1 and run only WindowEffectsWindowsTests.");

        AppBuilder.Configure<Kitopia.Desktop.App>().UsePlatformDetect().SetupWithoutStarting();
        var application = (Kitopia.Desktop.App)Application.Current!;
        var initialize = typeof(Kitopia.Desktop.App).GetMethod("InitializeWindowEffects", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var stop = typeof(Kitopia.Desktop.App).GetMethod("StopWindowEffects", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var settingGuid = (Guid)typeof(Kitopia.Desktop.App).GetField("EnergySaverSettingGuid", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var window = new Window { Classes = { "acrylic" }, ShowInTaskbar = false };
        var frame = new DispatcherFrame();
        uint? initialStatus = null;
        IntPtr ObservePowerBroadcast(IntPtr hwnd, uint msg, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            if (msg == (uint)User32.WindowMessage.WM_POWERBROADCAST &&
                wparam.ToInt64() == (long)User32.PowerBroadcastType.PBT_POWERSETTINGCHANGE && lparam != IntPtr.Zero)
            {
                var setting = lparam.ToStructure<User32.POWERBROADCAST_SETTING>();
                if (setting.PowerSetting == settingGuid && setting.DataLength == sizeof(uint))
                {
                    initialStatus = BitConverter.ToUInt32(setting.Data);
                    frame.Continue = false;
                }
            }
            return IntPtr.Zero;
        }

        Win32Properties.AddWndProcHookCallback(window, ObservePowerBroadcast);
        initialize.Invoke(application, [window]);
        try
        {
            var registration = (User32.SafeHPOWERSETTINGNOTIFY)typeof(Kitopia.Desktop.App)
                .GetField("_powerSettingNotification", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(application)!;
            Assert.IsFalse(registration.IsInvalid);
            using var timeout = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromSeconds(5));
            if (initialStatus is null)
                Dispatcher.UIThread.PushFrame(frame);
            Assert.IsNotNull(initialStatus, "Windows must send its initial energy saver status after registration.");
            TestContext.WriteLine($"Initial Windows energy saver status: {initialStatus}; setting: {settingGuid}");

            window.Show();
            Dispatcher.UIThread.RunJobs();
            var advancedEffects = new UISettings().AdvancedEffectsEnabled;
            Assert.AreEqual(initialStatus == 0 && advancedEffects ? 0.55 : 1.0, window.Background!.Opacity);
            var hwnd = new HWND(window.TryGetPlatformHandle()!.Handle);
            foreach (var status in new uint[] { 0, 1, 0, 2, 0 })
            {
                var setting = new User32.POWERBROADCAST_SETTING
                {
                    PowerSetting = settingGuid,
                    DataLength = sizeof(uint),
                    Data = BitConverter.GetBytes(status)
                };
                using var payload = SafeCoTaskMemHandle.CreateFromStructure(setting);
                User32.SendMessage(hwnd, User32.WindowMessage.WM_POWERBROADCAST,
                    (IntPtr)User32.PowerBroadcastType.PBT_POWERSETTINGCHANGE, payload.DangerousGetHandle());
                Dispatcher.UIThread.RunJobs();
                var enabled = status == 0 && advancedEffects;
                Assert.AreEqual(enabled ? 0.55 : 1.0, window.Background!.Opacity);
                Assert.AreEqual(enabled ? WindowTransparencyLevel.AcrylicBlur : WindowTransparencyLevel.None,
                    window.TransparencyLevelHint.Single());
                Assert.AreEqual(enabled ? WindowTransparencyLevel.AcrylicBlur : WindowTransparencyLevel.None,
                    window.ActualTransparencyLevel);
                var fallback = (ISolidColorBrush)window.TransparencyBackgroundFallback!;
                Assert.AreEqual(1.0, fallback.Opacity);
            }

            using var themeService = new ThemeChange();
            themeService.SetAccentColor(true, "#0064FA");
            Dispatcher.UIThread.RunJobs();
            var accent = new UISettings().GetColorValue(UIColorType.Accent);
            Assert.IsTrue(application.TryGetResource("KitopiaPrimaryBrush", ThemeVariant.Light, out var primary));
            Assert.AreEqual(Color.FromRgb(accent.R, accent.G, accent.B), ((ISolidColorBrush)primary!).Color,
                "The Avalonia system accent must match the actual Windows personalization accent.");
            TestContext.WriteLine($"Initial Windows accent color: #{accent.R:X2}{accent.G:X2}{accent.B:X2}");
        }
        finally
        {
            stop.Invoke(application, [window]);
            Win32Properties.RemoveWndProcHookCallback(window, ObservePowerBroadcast);
            window.Close();
        }
    }
}
#endif
