#if WINDOWS
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Kitopia.Desktop.Platform.Windows.AppTools;
using Vanara.PInvoke;

namespace KitopiaTest.Search;

[TestClass]
[DoNotParallelize]
public sealed class ExplorerFileOperationsWindowsTests
{
    [STATestMethod]
    [TestCategory("WindowsIntegration")]
    public void ShellFileOperations_NativeMenuAndClipboard_UseWindowsShell()
    {
        // Native Avalonia initialization must run separately from the headless suite.
        if (Environment.GetEnvironmentVariable("KITOPIA_RUN_WINDOWS_UI_TESTS") != "1")
            Assert.Inconclusive("Set KITOPIA_RUN_WINDOWS_UI_TESTS=1 and run only ExplorerFileOperationsWindowsTests.");

        AppBuilder.Configure<Kitopia.Desktop.App>().UsePlatformDetect().SetupWithoutStarting();
        var window = new Window { ShowInTaskbar = false };
        var directory = Directory.CreateTempSubdirectory("Kitopia Shell 文件 ");
        var originalClipboard = System.Windows.Clipboard.GetDataObject();
        try
        {
            window.Show();
            var source = Path.Combine(directory.FullName, "source 文档.txt");
            File.WriteAllText(source, "Shell copy and paste");
            ShellUtils.InvokeFileContextMenu(window, source, verb: "copy");
            Assert.IsTrue(System.Windows.Clipboard.ContainsFileDropList());
            Assert.AreEqual(source, System.Windows.Clipboard.GetFileDropList().Cast<string>().Single());

            // A file placed on the Windows clipboard can be pasted into the search directory.
            var target = directory.CreateSubdirectory("destination").FullName;
            var external = new System.Collections.Specialized.StringCollection { source };
            System.Windows.Clipboard.SetFileDropList(external);
            ShellUtils.InvokeFileContextMenu(window, target, verb: "paste");
            var copy = Path.Combine(target, Path.GetFileName(source));
            PumpUntil(() => File.Exists(copy));
            Assert.AreEqual(File.ReadAllText(source), File.ReadAllText(copy));

            ShellUtils.InvokeFileContextMenu(window, copy, verb: "cut");
            Assert.AreEqual(copy, System.Windows.Clipboard.GetFileDropList().Cast<string>().Single());
            var moved = directory.CreateSubdirectory("moved").FullName;
            ShellUtils.InvokeFileContextMenu(window, moved, verb: "paste");
            PumpUntil(() => File.Exists(Path.Combine(moved, Path.GetFileName(source))) && !File.Exists(copy));
            Assert.IsTrue(File.Exists(source), "Cut must move only the selected result.");

            var menuOpened = false;
            nint ObserveMenu(nint hwnd, uint message, nint wparam, nint lparam, ref bool handled)
            {
                if (message == (uint)User32.WindowMessage.WM_INITMENUPOPUP)
                {
                    menuOpened = true;
                    User32.PostMessage(hwnd, User32.WindowMessage.WM_CANCELMODE);
                }
                return 0;
            }
            Win32Properties.AddWndProcHookCallback(window, ObserveMenu);
            try
            {
                var hwnd = window.TryGetPlatformHandle()!.Handle;
                using var cancel = new Timer(_ => User32.PostMessage(hwnd, User32.WindowMessage.WM_CANCELMODE),
                    null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
                ShellUtils.InvokeFileContextMenu(window, source, window.PointToScreen(new Point(20, 20)));
                Assert.IsTrue(menuOpened, "The file's native Shell menu must open before cancellation.");
            }
            finally { Win32Properties.RemoveWndProcHookCallback(window, ObserveMenu); }
        }
        finally
        {
            if (originalClipboard is not null) System.Windows.Clipboard.SetDataObject(originalClipboard);
            else System.Windows.Clipboard.Clear();
            window.Close();
            directory.Delete(true);
        }
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var frame = new DispatcherFrame();
        using var poll = DispatcherTimer.Run(() =>
        {
            if (!condition()) return true;
            frame.Continue = false;
            return false;
        }, TimeSpan.FromMilliseconds(20));
        using var timeout = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromSeconds(10));
        Dispatcher.UIThread.PushFrame(frame);
        Assert.IsTrue(condition(), "Shell operation did not complete.");
    }
}
#endif
