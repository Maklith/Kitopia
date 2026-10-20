using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Utils;
using Kitopia.Desktop.Pages;
using Kitopia.Feature.Localization;
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
    public async Task ImageSaveDirectory_PickOrCancel_ShowsCurrentPathAndPersistsSelection()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SettingPageVisibilityTests));
        await session.Dispatch(async () =>
        {
            var originalServices = ServiceManager.Services;
            var originalConfigs = ConfigManger.Configs;
            var originalPluginConfig = KitopiaEx.Config.INSTANCE;
            var assembly = typeof(KitopiaEx.Config).Assembly;
            var needsRegistration = !Lang.Current.Keys.Contains("lang.kitopiaex.kitopiaex_settings");
            var picker = new FolderPicker();
            using var provider = new ServiceCollection().AddSingleton<IFeatureFilePicker>(picker).BuildServiceProvider();
            ServiceManager.Services = provider;
            var key = "test-image-save-directory-" + Guid.NewGuid().ToString("N");
            var config = new KitopiaEx.Config { Name = key, ImageSaveDirectory = " " };
            config.AfterLoad();
            ConfigManger.Configs = new Dictionary<string, ConfigBase> { [key] = config };
            var page = new SettingPage();
            var window = new Window { Content = page, Width = 900, Height = 850 };
            var configPath = KitopiaPaths.GetConfigFilePath(key);
            try
            {
                if (needsRegistration) Lang.Current.RegisterAssembly(assembly);
                page.ChangeConfig(config);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var row = page.GetLogicalDescendants().OfType<SettingsExpander>().Single(item => item.Footer is Grid);
                var control = (Grid)row.Footer!;
                var label = control.Children.OfType<TextBox>().Single();
                var button = control.Children.OfType<Button>().Single();
                var command = (IAsyncRelayCommand)button.Command!;
                Assert.AreEqual(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), label.Text);
                Assert.AreEqual(0, Grid.GetColumn(label));
                Assert.AreEqual(1, Grid.GetColumn(button));
                Assert.IsTrue(label.IsReadOnly);
                var changes = 0;
                config.ConfigChanged += (_, args) =>
                {
                    if (args.Name == nameof(config.ImageSaveDirectory)) changes++;
                };

                await command.ExecuteAsync(null);
                Assert.AreEqual(0, changes);
                Assert.AreEqual(config.ImageSaveDirectory, label.Text);
                Assert.IsFalse(picker.AllowMultiple);

                picker.Paths = [Path.Combine(Path.GetTempPath(), "Kitopia images", new string('x', 100))];
                await command.ExecuteAsync(null);
                Assert.AreEqual(picker.Paths[0], config.ImageSaveDirectory);
                Assert.AreEqual(config.ImageSaveDirectory, label.Text);
                Assert.AreEqual(label.Text, ToolTip.GetTip(label));
                await command.ExecuteAsync(null);
                Assert.AreEqual(1, changes);

                foreach (var displayPath in new[]
                         {
                             Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                             picker.Paths[0]
                         })
                foreach (var width in new[] { 900, 640 })
                {
                    picker.Paths = [displayPath];
                    await command.ExecuteAsync(null);
                    window.Width = width;
                    Dispatcher.UIThread.RunJobs();
                    row.BringIntoView();
                    Dispatcher.UIThread.RunJobs();
                    Assert.IsGreaterThan(0, label.Bounds.Width);
                    Assert.IsLessThanOrEqualTo(button.Bounds.X, label.Bounds.Right);
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var screenshot = window.CaptureRenderedFrame();
                    Assert.IsNotNull(screenshot);
                    var length = displayPath.Length > 80 ? "long" : "short";
                    var screenshotPath = Path.Combine(TestContext.TestRunDirectory!, $"settings-image-directory-{length}-{width}.png");
                    screenshot.Save(screenshotPath);
                    TestContext.AddResultFile(screenshotPath);
                }

                page.ChangeConfig(config);
                Assert.IsNull(button.Command);
                var restored = JsonSerializer.Deserialize<KitopiaEx.Config>(File.ReadAllText(configPath), ConfigManger.DefaultOptions)!;
                Assert.AreEqual(picker.Paths[0], restored.ImageSaveDirectory);
                var restoredControl = (Grid)page.GetLogicalDescendants().OfType<SettingsExpander>()
                    .Single(item => item.Footer is Grid).Footer!;
                Assert.AreEqual(restored.ImageSaveDirectory, restoredControl.Children.OfType<TextBox>().Single().Text);
            }
            finally
            {
                window.Close();
                ConfigManger.RemoveConfig(key);
                ConfigManger.Configs = originalConfigs;
                ServiceManager.Services = originalServices;
                KitopiaEx.Config.INSTANCE = originalPluginConfig;
                if (needsRegistration) Lang.Current.UnregisterAssembly(assembly.GetName().Name!);
                File.Delete(configPath);
                File.Delete(configPath + ".bak");
            }
            return true;
        }, CancellationToken.None);
    }

    private sealed class FolderPicker : IFeatureFilePicker
    {
        public IReadOnlyList<string> Paths { get; set; } = [];
        public bool AllowMultiple { get; private set; }

        public Task<IReadOnlyList<string>> PickFoldersAsync(string title, bool allowMultiple,
            CancellationToken cancellationToken = default)
        {
            AllowMultiple = allowMultiple;
            return Task.FromResult(Paths);
        }

        public Task<IReadOnlyList<string>> PickFilesAsync(string title, bool allowMultiple,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

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
