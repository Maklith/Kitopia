using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Utils;
using Kitopia.Desktop.Pages;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Config;
using PluginCore.CustomScenario.Attribute.ConfigField;
using SettingsExpander = Kitopia.Desktop.Controls.SettingsExpander.SettingsExpander;

namespace KitopiaTest.Services;

[TestClass]
[DoNotParallelize]
public sealed class SettingPageVisibilityTests
{
    public TestContext TestContext { get; set; } = null!;

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Kitopia.Desktop.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ConditionalSettings_ToggleSaveAndReopen_UpdateRowsAndReleaseOldSubscriptions(bool initiallyEnabled)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SettingPageVisibilityTests));
        await session.Dispatch(async () =>
        {
            var originalServices = ServiceManager.Services;
            var originalConfigs = ConfigManger.Configs;
            using var provider = new ServiceCollection().BuildServiceProvider();
            ServiceManager.Services = provider;
            var key = "test-settings-visibility-" + Guid.NewGuid().ToString("N");
            var config = new VisibilityConfig { Name = key, Enabled = initiallyEnabled };
            ConfigManger.Configs = new Dictionary<string, ConfigBase> { [key] = config };
            var page = new SettingPage();
            var window = new Window { Content = page, Width = 900, Height = 700 };
            var path = KitopiaPaths.GetConfigFilePath(key);
            try
            {
                page.ChangeConfig(config);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var rows = page.GetLogicalDescendants().OfType<SettingsExpander>().ToDictionary(row => (string)row.Header!);
                var toggle = (ToggleSwitch)rows["Enabled"].Footer!;
                var picker = (ColorPicker)rows["Accent"].Footer!;
                Assert.AreEqual(initiallyEnabled, rows["Accent"].IsVisible);
                Assert.AreEqual(!initiallyEnabled, rows["Fallback"].IsVisible);
                Assert.AreEqual(initiallyEnabled, ((Control)rows["Accent"].Parent!).IsVisible);
                Assert.AreEqual(!initiallyEnabled, ((Control)rows["Fallback"].Parent!).IsVisible);
                Assert.IsFalse(rows["Action"].IsVisible);
                Assert.IsFalse(picker.IsAlphaEnabled);

                foreach (var enabled in new[] { !initiallyEnabled, initiallyEnabled, true })
                {
                    toggle.IsChecked = enabled;
                    Assert.AreEqual(enabled, config.Enabled);
                    Assert.AreEqual(enabled, rows["Accent"].IsVisible);
                    Assert.AreEqual(!enabled, rows["Fallback"].IsVisible);
                    Assert.AreEqual(enabled, ((Control)rows["Accent"].Parent!).IsVisible);
                    Assert.AreEqual(!enabled, ((Control)rows["Fallback"].Parent!).IsVisible);
                }
                await Task.Run(() =>
                {
                    config.ActionsEnabled = true;
                    config.OnConfigChanged(this, nameof(VisibilityConfig.ActionsEnabled), true);
                });
                Dispatcher.UIThread.RunJobs();
                Assert.IsTrue(rows["Action"].IsVisible);
                picker.Color = Color.Parse("#33AA77");
                Assert.AreEqual("#33AA77", config.Accent);
                page.ChangeConfig(config);
                var restored = JsonSerializer.Deserialize<VisibilityConfig>(File.ReadAllText(path), ConfigManger.DefaultOptions)!;
                Assert.IsTrue(restored.Enabled);
                Assert.AreEqual("#33AA77", restored.Accent);
                restored.Name = key;
                ConfigManger.Configs[key] = restored;
                page.ChangeConfig(restored);
                var restoredRows = page.GetLogicalDescendants().OfType<SettingsExpander>().ToDictionary(row => (string)row.Header!);
                Assert.IsTrue(restoredRows["Accent"].IsVisible);
                Assert.AreEqual(picker.Color, ((ColorPicker)restoredRows["Accent"].Footer!).Color);

                config.OnConfigChanged(this, nameof(VisibilityConfig.Enabled), false);
                Assert.IsTrue(rows["Accent"].IsVisible, "Rebuilding the page must detach old configuration handlers.");
                Assert.IsTrue(restoredRows["Accent"].IsVisible, "Conditions must read only their own configuration instance.");
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var screenshot = window.CaptureRenderedFrame();
                Assert.IsNotNull(screenshot);
                var screenshotPath = Path.Combine(TestContext.TestRunDirectory!, $"settings-color-{initiallyEnabled}.png");
                screenshot.Save(screenshotPath);
                TestContext.AddResultFile(screenshotPath);
            }
            finally
            {
                window.Close();
                ConfigManger.RemoveConfig(key);
                ConfigManger.Configs = originalConfigs;
                ServiceManager.Services = originalServices;
                File.Delete(path);
                File.Delete(path + ".bak");
            }
            return true;
        }, CancellationToken.None);
    }

    [ConfigName("外观")]
    public sealed class VisibilityConfig : ConfigBase
    {
        [ConfigField("Enabled", "", fieldType: ConfigFieldType.布尔)]
        public bool Enabled;

        [ConfigFieldCategory("Enabled settings")]
        [ConfigField("Accent", "", fieldType: ConfigFieldType.颜色, VisibleWhen = nameof(Enabled))]
        public string Accent = "#0064FA";

        [ConfigFieldCategory("Disabled settings")]
        [ConfigField("Fallback", "", VisibleWhen = nameof(Enabled), VisibleWhenValue = false)]
        public string Fallback = "default";

        public bool ActionsEnabled { get; set; }

        [ConfigFieldCategory("Actions")]
        [ConfigField("Action", "", fieldType: ConfigFieldType.按钮, actionName: "Run", VisibleWhen = nameof(ActionsEnabled))]
        public void Run()
        {
        }
    }
}
