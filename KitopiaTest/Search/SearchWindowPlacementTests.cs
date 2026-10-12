using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Platform;
using Avalonia.Threading;
using Kitopia.Desktop.Windows;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace KitopiaTest.Search;

[TestClass]
[DoNotParallelize]
public sealed class SearchWindowPlacementTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Kitopia.Desktop.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

    [TestMethod]
    [DataRow(0, 0, 1920, 1040, 1d, 180d, 480, 260)]
    [DataRow(0, 0, 1920, 1040, 1d, 768d, 480, 260)]
    [DataRow(0, 0, 1920, 1040, 1.25d, 768d, 360, 80)]
    [DataRow(0, 0, 1920, 1040, 1.5d, 1040 / 1.5d, 240, 0)]
    [DataRow(0, 40, 1920, 1040, 1.25d, 768d, 360, 120)]
    [DataRow(-1920, -1080, 1920, 1040, 1.25d, 768d, -1560, -1000)]
    [DataRow(1920, 200, 2560, 1400, 2d, 700d, 2240, 200)]
    [DataRow(0, 0, 1920, 1040, 1.25d, 767.5d, 360, 80)]
    public void GetSearchWindowPosition_ScaledWindow_StaysWithinWorkingArea(
        int areaX, int areaY, int areaWidth, int areaHeight, double scaling, double height,
        int expectedX, int expectedY)
    {
        var method = typeof(SearchWindow).GetMethod("GetSearchWindowPosition", BindingFlags.Static | BindingFlags.NonPublic)!;
        var area = new PixelRect(areaX, areaY, areaWidth, areaHeight);
        var size = new Size(960, height);
        var position = (PixelPoint)method.Invoke(null, [area, scaling, size])!;

        Assert.AreEqual(new PixelPoint(expectedX, expectedY), position);
        Assert.IsTrue(area.Contains(new PixelRect(position, PixelSize.FromSize(size, scaling))),
            "The complete search window must remain above the taskbar and inside the selected display.");
    }

    [TestMethod]
    [DataRow(1.25d)]
    [DataRow(1.5d)]
    [DataRow(2d)]
    public async Task Show_ScaledWorkingArea_KeepsResizedWindowVisibleAndRestoresHeightOnReopen(double scaling)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SearchWindowPlacementTests));
        await session.Dispatch(() =>
        {
            var originalServices = ServiceManager.Services;
            using var services = new ServiceCollection()
                .AddSingleton(DispatchProxy.Create<IWindowTool, WindowToolProxy>()).BuildServiceProvider();
            ServiceManager.Services = services;
            var window = new SearchWindow { SizeToContent = SizeToContent.Manual, Height = 768 };
            var screen = window.Screens.Primary!;
            var originalScaling = screen.Scaling;
            var originalWorkingArea = screen.WorkingArea;
            var scalingProperty = typeof(Screen).GetProperty(nameof(Screen.Scaling))!;
            var workingAreaProperty = typeof(Screen).GetProperty(nameof(Screen.WorkingArea))!;
            window.Closing -= typeof(SearchWindow).GetMethod("Window_OnClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<EventHandler<WindowClosingEventArgs>>(window);
            try
            {
                scalingProperty.SetValue(screen, scaling);
                workingAreaProperty.SetValue(screen, new PixelRect(0, 40, 1920, 1000));
                window.SetRenderScaling(scaling);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.IsTrue(screen.WorkingArea.Contains(new PixelRect(window.Position,
                    PixelSize.FromSize(window.Bounds.Size, scaling))));
                Assert.IsTrue(window.Bounds.Height <= screen.WorkingArea.Height / scaling);

                window.Height = 180;
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(290, window.Position.Y);
                window.Height = 768;
                Dispatcher.UIThread.RunJobs();
                Assert.IsTrue(screen.WorkingArea.Contains(new PixelRect(window.Position,
                    PixelSize.FromSize(window.Bounds.Size, scaling))),
                    "Growing the visible window must keep it inside the work area.");

                window.Hide();
                scalingProperty.SetValue(screen, 1d);
                window.SetRenderScaling(1);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(768, window.MaxHeight);
                window.Height = 768;
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(768, window.Bounds.Height);
                Assert.IsTrue(screen.WorkingArea.Contains(new PixelRect(window.Position,
                    PixelSize.FromSize(window.Bounds.Size, 1))));
            }
            finally
            {
                window.Close();
                scalingProperty.SetValue(screen, originalScaling);
                workingAreaProperty.SetValue(screen, originalWorkingArea);
                ServiceManager.Services = originalServices;
            }

            return true;
        }, CancellationToken.None);
    }

    private class WindowToolProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod!.Name switch
        {
            nameof(IWindowTool.MoveWindowToMouseScreenCenter) or nameof(IWindowTool.SetForegroundWindow) => null,
            _ => throw new NotSupportedException(targetMethod.Name)
        };
    }
}
