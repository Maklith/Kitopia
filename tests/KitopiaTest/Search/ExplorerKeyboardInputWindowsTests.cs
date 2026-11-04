#if WINDOWS
using System.Collections.Concurrent;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Kitopia.Desktop.Platform.Windows;
using SharpHook;
using SharpHook.Data;
using Vanara.PInvoke;

namespace KitopiaTest.Search;

[TestClass]
[DoNotParallelize]
public sealed class ExplorerKeyboardInputWindowsTests
{
    [STATestMethod]
    [TestCategory("WindowsIntegration")]
    public void KeyboardInput_InjectedVirtualKeysTriggerButUnicodeTextPassesThrough()
    {
        if (Environment.GetEnvironmentVariable("KITOPIA_RUN_WINDOWS_UI_TESTS") != "1")
            Assert.Inconclusive("Set KITOPIA_RUN_WINDOWS_UI_TESTS=1 and run only ExplorerKeyboardInputWindowsTests.");

        AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithoutStarting();
        var window = new Window { ShowInTaskbar = false, Width = 200, Height = 100 };
        using var hook = new SimpleGlobalHook();
        var events = new ConcurrentQueue<KeyboardHookEventArgs>();
        var getCharacter = typeof(HotKeyImpl).GetMethod("GetCharacter", BindingFlags.Static | BindingFlags.NonPublic)!;
        try
        {
            window.Show();
            window.Activate();
            var hwnd = window.TryGetPlatformHandle()!.Handle;
            hook.KeyPressed += (_, e) =>
            {
                if (User32.GetForegroundWindow() == (HWND)hwnd && e.IsEventSimulated) events.Enqueue(e);
            };
            var ready = new DispatcherFrame();
            hook.HookEnabled += (_, _) => Dispatcher.UIThread.Post(() => ready.Continue = false);
            _ = hook.RunAsync();
            using (DispatcherTimer.RunOnce(() => ready.Continue = false, TimeSpan.FromSeconds(5)))
                Dispatcher.UIThread.PushFrame(ready);
            Assert.IsTrue(hook.IsRunning);
            foreach (var textPacket in new[] { false, true })
            {
                var frame = new DispatcherFrame();
                using var poll = DispatcherTimer.Run(() =>
                {
                    if (events.IsEmpty) return true;
                    frame.Continue = false;
                    return false;
                }, TimeSpan.FromMilliseconds(20));
                using var timeout = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromSeconds(5));
                var simulator = new EventSimulator();
                if (textPacket) Assert.AreEqual(UioHookResult.Success, simulator.SimulateTextEntry("A"));
                else
                {
                    Assert.AreEqual(UioHookResult.Success, simulator.SimulateKeyPress(KeyCode.VcA));
                    Assert.AreEqual(UioHookResult.Success, simulator.SimulateKeyRelease(KeyCode.VcA));
                }
                Dispatcher.UIThread.PushFrame(frame);
                Assert.IsTrue(events.TryDequeue(out var input), "The real Windows keyboard hook must receive the injected event.");
                Assert.AreEqual(textPacket ? (ushort)User32.VK.VK_PACKET : (ushort)User32.VK.VK_A, input.Data.RawCode);
                var character = (string?)getCharacter.Invoke(null, [input.Data.RawCode, input.RawEvent.Mask, (HWND)hwnd]);
                if (textPacket) Assert.IsNull(character, "Forwarded Unicode text must not trigger a shortcut again.");
                else Assert.AreEqual("a", character?.ToLowerInvariant());
            }
        }
        finally { window.Close(); }
    }
}
#endif
