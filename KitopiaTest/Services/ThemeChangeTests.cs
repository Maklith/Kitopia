using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Kitopia.Desktop.Services;

namespace KitopiaTest.Services;

[TestClass]
[DoNotParallelize]
public sealed class ThemeChangeTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Kitopia.Desktop.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia()
        .AfterPlatformServicesSetup(_ =>
        {
            // Avalonia 12 hides platform registration APIs in its reference assemblies.
            var locator = typeof(AvaloniaLocator).GetProperty("CurrentMutable")!.GetValue(null)!;
            var settings = DispatchProxy.Create<IPlatformSettings, TestPlatformSettings>();
            ((TestPlatformSettings)settings).Platform = (IPlatformSettings)typeof(AvaloniaLocator)
                .GetMethod("GetService")!.Invoke(locator, [typeof(IPlatformSettings)])!;
            var registration = typeof(AvaloniaLocator).GetMethod("Bind")!.MakeGenericMethod(typeof(IPlatformSettings))
                .Invoke(locator, null)!;
            registration.GetType().GetMethod("ToConstant")!.MakeGenericMethod(typeof(IPlatformSettings))
                .Invoke(registration, [settings]);
        });

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task FollowSys_ManualThemeSelected_AppliesCurrentSystemThemeAndTracksChanges(bool systemDark, bool backgroundThread)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ThemeChangeTests));
        await session.Dispatch(async () =>
        {
            var application = Application.Current!;
            SetSystemTheme(systemDark);
            var service = new ThemeChange();
            service.changeTo(systemDark ? "theme_light" : "theme_dark");
            Dispatcher.UIThread.RunJobs();
            var window = new Window();
            try
            {
                window.Show();
                Assert.AreEqual(systemDark ? ThemeVariant.Light : ThemeVariant.Dark, window.ActualThemeVariant);

                if (backgroundThread)
                    await Task.Run(() => service.followSys(true));
                else
                    service.followSys(true);
                Dispatcher.UIThread.RunJobs();

                Assert.AreEqual(systemDark ? ThemeVariant.Dark : ThemeVariant.Light, application.ActualThemeVariant);
                Assert.AreEqual(application.ActualThemeVariant, window.ActualThemeVariant);
                Assert.AreEqual(systemDark, service.isDark());

                SetSystemTheme(!systemDark);
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(systemDark ? ThemeVariant.Light : ThemeVariant.Dark, window.ActualThemeVariant);
                Assert.AreEqual(!systemDark, service.isDark());
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FollowSys_Disabled_RetainsCurrentThemeUntilManualSelection(bool systemDark)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ThemeChangeTests));
        await session.Dispatch(() =>
        {
            var application = Application.Current!;
            var service = new ThemeChange();
            SetSystemTheme(systemDark);
            service.followSys(true);
            Dispatcher.UIThread.RunJobs();

            service.followSys(false);
            Dispatcher.UIThread.RunJobs();
            SetSystemTheme(!systemDark);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(systemDark ? ThemeVariant.Dark : ThemeVariant.Light, application.ActualThemeVariant);

            service.changeTo(systemDark ? "theme_light" : "theme_dark");
            Dispatcher.UIThread.RunJobs();
            SetSystemTheme(systemDark);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(systemDark ? ThemeVariant.Light : ThemeVariant.Dark, application.ActualThemeVariant);
            Assert.AreEqual(!systemDark, service.isDark());
            return true;
        }, CancellationToken.None);
    }

    private static void SetSystemTheme(bool dark)
    {
        var settings = (TestPlatformSettings)Application.Current!.PlatformSettings!;
        settings.Colors = new PlatformColorValues { ThemeVariant = dark ? PlatformThemeVariant.Dark : PlatformThemeVariant.Light };
        typeof(DefaultPlatformSettings).GetMethod("OnColorValuesChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(settings.Platform, [settings.Colors]);
    }

    private class TestPlatformSettings : DispatchProxy
    {
        public IPlatformSettings Platform { get; set; } = null!;
        public PlatformColorValues Colors { get; set; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod!.Name == nameof(IPlatformSettings.GetColorValues) ? Colors : targetMethod.Invoke(Platform, args);
    }
}
