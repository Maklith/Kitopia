#if WINDOWS
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Platform.Windows;
using PluginCore;
using PluginCore.Config;
using Vanara.PInvoke;

namespace KitopiaTest.Search;

[TestClass]
[DoNotParallelize]
public sealed class FullScreenHotkeyWindowsTests
{
    [TestMethod]
    [DataRow(HotKeyType.Keyboard, MouseHotKeyTrigger.Hold)]
    [DataRow(HotKeyType.Mouse, MouseHotKeyTrigger.Hold)]
    [DataRow(HotKeyType.Mouse, MouseHotKeyTrigger.DragRelease)]
    public async Task Hotkey_FullScreenWindow_RespectsOwnScopeWindowStyleAndMonitorBounds(HotKeyType type, MouseHotKeyTrigger trigger)
    {
        // A hidden window in another process exercises real Win32 queries without taking foreground focus.
        const string script = """
            Add-Type -AssemblyName System.Windows.Forms
            $window = New-Object System.Windows.Forms.Form
            $window.ShowInTaskbar = $false
            [Console]::WriteLine($window.Handle.ToInt64())
            [System.Windows.Forms.Application]::Run()
            """;
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process = Process.Start(start)!;
        var originalConfigs = ConfigManger.Configs;
        HWND ownWindow = default;
        try
        {
            var output = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.IsTrue(long.TryParse(output, out var handle), $"Window helper failed: {output}");
            var window = new HWND(new IntPtr(handle));
            var config = new KitopiaConfig();
            var model = new HotKeyModel { MainName = "TestPlugin", IsEnabled = true,
                Type = type, MouseTrigger = trigger, DisableInFullScreen = true };
            ConfigManger.Configs = new Dictionary<string, ConfigBase> { ["KitopiaConfig"] = config };
            var canExecute = typeof(HotKeyImpl).GetMethod("CanExecuteInWindow", BindingFlags.NonPublic | BindingFlags.Static)!;
            bool Allowed(HotKeyModel model, HWND target) => (bool)canExecute.Invoke(null, [model, target])!;
            var monitors = new List<RECT>();
            unsafe
            {
                Assert.IsTrue(User32.EnumDisplayMonitors(default, null, (monitor, _, _, _) =>
                {
                    var info = new User32.MONITORINFO();
                    Assert.IsTrue(User32.GetMonitorInfo(monitor, ref info));
                    monitors.Add(info.rcMonitor);
                    return true;
                }, IntPtr.Zero));
            }
            Assert.IsNotEmpty(monitors);
            foreach (var bounds in monitors)
            {
                User32.SetWindowLong(window, User32.WindowLongFlags.GWL_STYLE, unchecked((int)User32.WindowStyles.WS_POPUP));
                Assert.IsTrue(User32.SetWindowPos(window, default, bounds.left, bounds.top, bounds.Width, bounds.Height,
                    User32.SetWindowPosFlags.SWP_NOACTIVATE | User32.SetWindowPosFlags.SWP_NOZORDER | User32.SetWindowPosFlags.SWP_FRAMECHANGED));
                Assert.IsTrue(WindowToolServiceWindow.IsFullScreenWindow(window));
                Assert.IsFalse(Allowed(model, window));
                Assert.IsTrue(Allowed(new HotKeyModel { IsEnabled = true }, window), "Other shortcuts must remain available.");
                model.DisableInFullScreen = false;
                Assert.IsTrue(Allowed(model, window));
                Assert.IsFalse(Allowed(config.searchHotKey, window), "Changing one shortcut must not affect another.");
                model.DisableInFullScreen = true;
                Assert.IsFalse(Allowed(model, window));
                Assert.IsTrue(model.IsEnabled, "Temporary suppression must preserve enabled intent.");

                // A captioned maximized window remains searchable, even with an auto-hidden taskbar.
                User32.SetWindowLong(window, User32.WindowLongFlags.GWL_STYLE,
                    unchecked((int)(User32.WindowStyles.WS_POPUP | User32.WindowStyles.WS_CAPTION | User32.WindowStyles.WS_MAXIMIZE)));
                Assert.IsTrue(Allowed(model, window));
                User32.SetWindowLong(window, User32.WindowLongFlags.GWL_STYLE,
                    unchecked((int)(User32.WindowStyles.WS_POPUP | User32.WindowStyles.WS_MAXIMIZE)));
                Assert.IsFalse(Allowed(model, window), "Borderless full screen can retain maximized state.");

                foreach (var inset in new[] { 2, 3, 20 })
                {
                    Assert.IsTrue(User32.SetWindowPos(window, default, bounds.left + inset, bounds.top + inset,
                        bounds.Width - 2 * inset, bounds.Height - 2 * inset,
                        User32.SetWindowPosFlags.SWP_NOACTIVATE | User32.SetWindowPosFlags.SWP_NOZORDER));
                    Assert.AreEqual(inset <= 2, WindowToolServiceWindow.IsFullScreenWindow(window));
                    Assert.AreEqual(inset > 2, Allowed(model, window));
                }
            }
            var primary = monitors[0];
            ownWindow = User32.CreateWindowEx(0, "STATIC", "Kitopia fullscreen test", User32.WindowStyles.WS_POPUP,
                primary.left, primary.top, primary.Width, primary.Height, default, default, default, IntPtr.Zero);
            Assert.IsFalse(ownWindow.IsNull);
            Assert.IsTrue(WindowToolServiceWindow.IsFullScreenWindow(ownWindow));
            Assert.IsTrue(Allowed(model, ownWindow));
            Assert.IsTrue(Allowed(model, User32.GetDesktopWindow()));
            Assert.IsTrue(Allowed(model, User32.GetShellWindow()));
            Assert.IsTrue(Allowed(model, default));
            Assert.IsFalse(WindowToolServiceWindow.IsFullScreenWindow(default));
            Assert.IsFalse(WindowToolServiceWindow.IsFullScreenWindow(new HWND(new IntPtr(-1))));
        }
        finally
        {
            if (!ownWindow.IsNull) User32.DestroyWindow(ownWindow);
            ConfigManger.Configs = originalConfigs;
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }
}
#endif
