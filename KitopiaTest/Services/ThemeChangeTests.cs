using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
        => SetSystemColors(dark, Color.FromRgb(0, 100, 250));

    private static void SetSystemColors(bool dark, Color accent)
    {
        var settings = (TestPlatformSettings)Application.Current!.PlatformSettings!;
        settings.Colors = new PlatformColorValues
        {
            ThemeVariant = dark ? PlatformThemeVariant.Dark : PlatformThemeVariant.Light,
            AccentColor1 = accent
        };
        typeof(DefaultPlatformSettings).GetMethod("OnColorValuesChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(settings.Platform, [settings.Colors]);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task SetAccentColor_FollowSystem_AppliesCurrentColorAndTracksChanges(bool dark, bool backgroundThread)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ThemeChangeTests));
        await session.Dispatch(async () =>
        {
            var application = Application.Current!;
            var firstColor = Color.Parse("#107C41");
            SetSystemColors(dark, firstColor);
            using var service = new ThemeChange();
            if (backgroundThread)
                await Task.Run(() => service.SetAccentColor(true, "#0064FA"));
            else
                service.SetAccentColor(true, "#0064FA");
            Dispatcher.UIThread.RunJobs();
            var button = new Button
            {
                Theme = (ControlTheme)application.FindResource("SolidButton")!, Classes = { "Primary" },
                Content = "Primary", Width = 150, Height = 32
            };
            var window = new Window { Content = button };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var firstClientBrush = (ISolidColorBrush)application.FindResource(application.ActualThemeVariant, "KitopiaPrimaryBrush")!;
                var firstPrimary = firstClientBrush.Color;
                var firstBrush = button.Background;
                Assert.AreEqual(firstColor.ToHsl().H, firstPrimary.ToHsl().H, 1.0);
                if (!dark)
                    Assert.AreEqual(firstColor, firstPrimary);
                Assert.AreEqual(firstPrimary, ((ISolidColorBrush)button.Background!).Color,
                    "Semi controls must use the same accent as Kitopia controls.");

                var nextColor = Color.Parse("#C42B1C");
                SetSystemColors(dark, nextColor);
                Dispatcher.UIThread.RunJobs();
                var nextPrimary = ((ISolidColorBrush)application.FindResource(application.ActualThemeVariant, "KitopiaPrimaryBrush")!).Color;
                Assert.AreNotEqual(firstPrimary, nextPrimary);
                Assert.AreEqual(nextColor.ToHsl().H, nextPrimary.ToHsl().H, 1.0);
                Assert.AreEqual(nextPrimary, ((ISolidColorBrush)button.Background!).Color,
                    "Existing controls must refresh when the system accent changes.");
                Assert.AreSame(firstBrush, button.Background, "Accent updates must retain existing brushes.");
                Assert.AreSame(firstClientBrush, application.FindResource(application.ActualThemeVariant, "KitopiaPrimaryBrush"),
                    "Kitopia tokens must update through their base colors without replacing brushes.");

                var presenter = button.GetVisualDescendants().OfType<ContentPresenter>()
                    .Single(item => item.Name == "PART_ContentPresenter");
                var point = button.TranslatePoint(new Point(10, 10), window)!.Value;
                window.MouseMove(point);
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(((ISolidColorBrush)application.FindResource(application.ActualThemeVariant, "KitopiaPrimaryHoverBrush")!).Color,
                    ((ISolidColorBrush)presenter.Background!).Color);
                window.MouseDown(point, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(((ISolidColorBrush)application.FindResource(application.ActualThemeVariant, "KitopiaPrimaryActiveBrush")!).Color,
                    ((ISolidColorBrush)presenter.Background!).Color);
                window.MouseUp(point, MouseButton.Left);
                window.MouseMove(new Point(0, 0));
                Dispatcher.UIThread.RunJobs();

                service.Dispose();
                SetSystemColors(dark, firstColor);
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(nextPrimary, ((ISolidColorBrush)button.Background!).Color);
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task SetAccentColor_CustomColor_PreservesSelectionAcrossSystemChangesAndThemeSwitches()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ThemeChangeTests));
        await session.Dispatch(() =>
        {
            var application = Application.Current!;
            using var service = new ThemeChange();
            SetSystemColors(false, Color.Parse("#C42B1C"));
            service.SetAccentColor(true, "#0064FA");
            Dispatcher.UIThread.RunJobs();
            service.SetAccentColor(false, "#107C41");
            Dispatcher.UIThread.RunJobs();
            var customColor = Color.Parse("#107C41");
            Assert.AreEqual(customColor, ((ISolidColorBrush)application.FindResource("SemiColorPrimary")!).Color);
            SetSystemColors(false, Color.Parse("#0064FA"));
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(customColor, ((ISolidColorBrush)application.FindResource("KitopiaPrimaryBrush")!).Color);
            service.changeTo("theme_dark");
            Dispatcher.UIThread.RunJobs();
            var darkColor = ((ISolidColorBrush)application.FindResource(application.ActualThemeVariant, "KitopiaPrimaryBrush")!).Color;
            Assert.IsTrue(darkColor.ToHsl().L > customColor.ToHsl().L);
            Assert.AreEqual(darkColor, ((ISolidColorBrush)application.FindResource(application.ActualThemeVariant, "SemiColorPrimary")!).Color);
            service.changeTo("theme_light");
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(customColor, ((ISolidColorBrush)application.FindResource("KitopiaPrimaryBrush")!).Color);
            service.SetAccentColor(false, "invalid");
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(Color.Parse("#0064FA"), ((ISolidColorBrush)application.FindResource("KitopiaPrimaryBrush")!).Color);
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AccentResources_BasePaletteChanged_RefreshesExistingBrushesInExplicitTheme(bool dark)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(ThemeChangeTests));
        await session.Dispatch(() =>
        {
            var application = Application.Current!;
            using var service = new ThemeChange();
            service.SetAccentColor(false, "#107C41");
            service.changeTo(dark ? "theme_light" : "theme_dark");
            Dispatcher.UIThread.RunJobs();
            var variant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var brushes = new[]
            {
                "KitopiaPrimaryBrush", "KitopiaBadgeCurrentForeground", "KitopiaBadgeTagForeground",
                "KitopiaAvatarForeground", "ToggleSwitchContainerCheckedBackground",
                "KitopiaPrimaryHoverBrush", "ToggleSwitchContainerCheckedPointeroverBackground",
                "KitopiaPrimaryActiveBrush", "ToggleSwitchContainerCheckedPressedBackground",
                "KitopiaPrimaryLightBrush", "KitopiaBadgeCurrentBackground", "KitopiaBadgeTagBackground",
                "KitopiaAvatarBackground", "ListBoxItemSelectedBackground",
                "KitopiaBadgeTagHoverBackground", "ListBoxItemSelectedPointeroverBackground"
            }.ToDictionary(key => key, key => (ISolidColorBrush)application.FindResource(variant, key)!);
            var button = new Button
            {
                Theme = (ControlTheme)application.FindResource("SolidButton")!, Classes = { "Primary" },
                Content = "Primary", Width = 150, Height = 32
            };
            var window = new Window { RequestedThemeVariant = variant, Content = button };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var primary = Color.Parse("#CB7854");
                var hover = Color.Parse("#B86542");
                var active = Color.Parse("#A45230");
                var light = dark ? primary : Color.Parse("#FBE9E1");
                var lightHover = dark ? primary : Color.Parse("#F4D2C4");
                var resources = (IResourceDictionary)application.Resources.ThemeDictionaries[dark ? ThemeVariant.Dark : ThemeVariant.Default];
                resources["KitopiaAccentColor"] = primary;
                resources["KitopiaAccentHoverColor"] = hover;
                resources["KitopiaAccentActiveColor"] = active;
                if (!dark)
                {
                    resources["KitopiaAccentLightColor"] = light;
                    resources["KitopiaAccentLightHoverColor"] = lightHover;
                }
                Dispatcher.UIThread.RunJobs();

                foreach (var (key, brush) in brushes)
                {
                    Assert.AreSame(brush, application.FindResource(variant, key), key);
                    var expected = key switch
                    {
                        "KitopiaPrimaryHoverBrush" or "ToggleSwitchContainerCheckedPointeroverBackground" => hover,
                        "KitopiaPrimaryActiveBrush" or "ToggleSwitchContainerCheckedPressedBackground" => active,
                        "KitopiaBadgeTagHoverBackground" or "ListBoxItemSelectedPointeroverBackground" => lightHover,
                        "KitopiaPrimaryLightBrush" or "KitopiaBadgeCurrentBackground" or "KitopiaBadgeTagBackground"
                            or "KitopiaAvatarBackground" or "ListBoxItemSelectedBackground" => light,
                        _ => primary
                    };
                    Assert.AreEqual(expected, brush.Color, key);
                }
                Assert.AreEqual(dark ? 0.15 : 1, brushes["KitopiaAvatarBackground"].Opacity);
                Assert.AreEqual(dark ? 0.22 : 1, brushes["KitopiaBadgeTagHoverBackground"].Opacity);
                Assert.AreEqual(primary, ((ISolidColorBrush)button.Background!).Color,
                    "The window's explicit theme must retain its own palette when the application uses another theme.");
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    private class TestPlatformSettings : DispatchProxy
    {
        public IPlatformSettings Platform { get; set; } = null!;
        public PlatformColorValues Colors { get; set; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod!.Name == nameof(IPlatformSettings.GetColorValues) ? Colors : targetMethod.Invoke(Platform, args);
    }
}
