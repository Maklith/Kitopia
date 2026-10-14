using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
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
public sealed class SettingPageSaveTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Kitopia.Desktop.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

    [TestMethod]
    public async Task Settings_InitializationAndRapidEdits_SaveOnceAndFlushOnUnload()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SettingPageSaveTests));
        await session.Dispatch(async () =>
        {
            var originalServices = ServiceManager.Services;
            var originalConfigs = ConfigManger.Configs;
            using var provider = new ServiceCollection().BuildServiceProvider();
            ServiceManager.Services = provider;
            var key = "test-settings-save-" + Guid.NewGuid().ToString("N");
            var config = new SavingConfig { Name = key };
            ConfigManger.Configs = new Dictionary<string, ConfigBase> { [key] = config };
            var path = KitopiaPaths.GetConfigFilePath(key);
            var originalJson = JsonSerializer.Serialize(config, ConfigManger.DefaultOptions);
            File.WriteAllText(path, originalJson);
            var changes = 0;
            config.ConfigChanged += (_, _) => changes++;
            var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var recipient = new object();
            var notifications = 0;
            WeakReferenceMessenger.Default.Register<string, string>(recipient, "ConfigSave", (_, _) =>
            {
                notifications++;
                saved.TrySetResult();
            });
            var page = new SettingPage();
            var window = new Window { Content = page, Width = 900, Height = 700 };
            try
            {
                page.ChangeConfig(config);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual(0, changes, "Opening settings must not publish changes or save initialized controls.");
                Assert.AreEqual(0, config.SaveCount);
                Assert.AreEqual(originalJson, File.ReadAllText(path));
                Assert.IsFalse(File.Exists(path + ".bak"));

                var rows = page.GetLogicalDescendants().OfType<SettingsExpander>().ToDictionary(row => (string)row.Header!);
                var textBox = (TextBox)rows["Title"].Footer!;
                var slider = ((Panel)rows["Level"].Footer!).Children.OfType<Slider>().Single();
                textBox.Text = "A";
                textBox.Text = "AB";
                textBox.Text = "ABC";
                for (var value = 1; value <= 20; value++) slider.Value = value;
                Assert.AreEqual("ABC", config.Title);
                Assert.AreEqual(20, config.Level);
                Assert.AreEqual(0, config.SaveCount);
                Assert.AreEqual(0, notifications);
                await saved.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.AreEqual(1, config.SaveCount);
                Assert.AreEqual(1, notifications);
                var restored = JsonSerializer.Deserialize<SavingConfig>(File.ReadAllText(path), ConfigManger.DefaultOptions)!;
                Assert.AreEqual("ABC", restored.Title);
                Assert.AreEqual(20, restored.Level);
                Assert.AreEqual(originalJson, File.ReadAllText(path + ".bak"));

                textBox.Text = "final";
                window.Content = null;
                Dispatcher.UIThread.RunJobs();
                Assert.AreEqual("final", JsonSerializer.Deserialize<SavingConfig>(File.ReadAllText(path), ConfigManger.DefaultOptions)!.Title);
                Assert.AreEqual(2, config.SaveCount);
                Assert.AreEqual(2, notifications);
            }
            finally
            {
                window.Close();
                WeakReferenceMessenger.Default.UnregisterAll(recipient);
                ConfigManger.RemoveConfig(key);
                ConfigManger.Configs = originalConfigs;
                ServiceManager.Services = originalServices;
                File.Delete(path);
                File.Delete(path + ".bak");
            }
            return true;
        }, CancellationToken.None);
    }

    public sealed class SavingConfig : ConfigBase
    {
        [ConfigField("Title", "")]
        public string Title = "initial";

        [ConfigField("Enabled", "", fieldType: ConfigFieldType.布尔)]
        public bool Enabled;

        [ConfigField("Count", "", fieldType: ConfigFieldType.整数, maxValue: 100)]
        public int Count = 5;

        [ConfigField("Level", "", fieldType: ConfigFieldType.整数滑块, maxValue: 100)]
        public int Level;

        [ConfigField("Ratio", "", fieldType: ConfigFieldType.浮点数, maxValue: 100)]
        public double Ratio = 1;

        [ConfigField<HotKeyType>("Mode", "")]
        public HotKeyType Mode = HotKeyType.Mouse;

        [JsonIgnore] public int SaveCount;
        public override void BeforeSave() => SaveCount++;
    }
}
