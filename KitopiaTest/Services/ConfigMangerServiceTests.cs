using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Utils;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Config;

namespace KitopiaTest.Services;

[TestClass]
[DoNotParallelize]
public sealed class ConfigMangerServiceTests
{
    [TestMethod]
    public void ConfigManger_ExposesManagerStateThroughConfigService()
    {
        var originalConfigs = ConfigManger.Configs;

        try
        {
            var config = new KitopiaConfig { Name = "KitopiaConfig" };
            ConfigManger.Configs = new Dictionary<string, ConfigBase>
            {
                ["KitopiaConfig"] = config
            };

            IConfigService service = new ConfigManger();

            Assert.AreEqual(ConfigManger.Version, service.Version);
            Assert.AreEqual(ConfigManger.ApiUrl, service.ApiUrl);
            Assert.AreSame(ConfigManger.AllConfigs, service.Configs);
            Assert.AreSame(ConfigManger.Config, service.Config);
            Assert.AreSame(ConfigManger.DefaultOptions, service.DefaultOptions);
            Assert.AreEqual(50, config.indexingMaximumCpuUsagePercent);
        }
        finally
        {
            ConfigManger.Configs = originalConfigs;
        }
    }

    [TestMethod]
    public void ConfigManger_WhenUseLocalhostDebugEnabled_ReturnsLocalhostUrls()
    {
#if DEBUG
        var originalConfigs = ConfigManger.Configs;
        try
        {
            var config = new KitopiaConfig { Name = "KitopiaConfig", useLocalhostDebug = true };
            ConfigManger.Configs = new Dictionary<string, ConfigBase>
            {
                ["KitopiaConfig"] = config
            };

            Assert.AreEqual("https://localhost:5111", ConfigManger.ApiUrl);
            Assert.AreEqual("http://localhost:3000", ConfigManger.WebUrl);

            config.useLocalhostDebug = false;
            Assert.AreEqual("https://api.kitopia.top:5111", ConfigManger.ApiUrl);
            Assert.AreEqual("https://kitopia.top", ConfigManger.WebUrl);
        }
        finally
        {
            ConfigManger.Configs = originalConfigs;
        }
#endif
    }

    [TestMethod]
    public void MigrateConfig_LegacyConfig_MigratesCollectionsAndPreviewScope()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), $"kitopia-migration-{Guid.NewGuid():N}");
        var filePath = Path.Combine(Path.GetTempPath(), $"kitopia-migration-{Guid.NewGuid():N}.txt");
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(filePath, string.Empty);

        try
        {
            var json = JsonSerializer.Serialize(new
            {
                customCollections = new[] { directoryPath, directoryPath, filePath },
                mouseHotkey = new { }
            });
            using var document = JsonDocument.Parse(json);
            var config = JsonSerializer.Deserialize<KitopiaConfig>(json, ConfigManger.DefaultOptions)!;

            ConfigManger.MigrateConfig(document.RootElement, config);

            Assert.AreEqual(config.CurrentConfigVersion, config.ConfigVersion);
            CollectionAssert.AreEqual(new[] { directoryPath }, config.managedIndexDirectories.ToArray());
            CollectionAssert.AreEqual(new[] { filePath }, config.managedIndexFiles.ToArray());
            Assert.AreEqual(HotKeyProcessScope.Include, config.mouseHotkey.ProcessScope);
            CollectionAssert.AreEqual(new[] { "explorer.exe" }, config.mouseHotkey.ProcessNames);
            Assert.IsTrue(config.mouseHotkey.IgnoreTextInput);
        }
        finally
        {
            Directory.Delete(directoryPath, true);
            File.Delete(filePath);
        }
    }

    [TestMethod]
    public void MigrateConfig_CurrentConfig_PreservesExplicitPreviewScope()
    {
        using var document = JsonDocument.Parse("{\"mouseHotkey\":{}}");
        var config = new KitopiaConfig { ConfigVersion = new KitopiaConfig().CurrentConfigVersion };
        config.mouseHotkey.ProcessScope = HotKeyProcessScope.Exclude;
        config.mouseHotkey.ProcessNames = ["notepad.exe"];
        config.mouseHotkey.IgnoreTextInput = false;

        ConfigManger.MigrateConfig(document.RootElement, config);

        Assert.AreEqual(HotKeyProcessScope.Exclude, config.mouseHotkey.ProcessScope);
        CollectionAssert.AreEqual(new[] { "notepad.exe" }, config.mouseHotkey.ProcessNames);
        Assert.IsFalse(config.mouseHotkey.IgnoreTextInput);
    }

    [TestMethod]
    public void MigrateConfig_FutureConfig_PreservesLoadedValues()
    {
        using var document = JsonDocument.Parse("{\"mouseHotkey\":{}}");
        var config = new KitopiaConfig { ConfigVersion = new KitopiaConfig().CurrentConfigVersion + 1 };
        config.mouseHotkey.ProcessScope = HotKeyProcessScope.Exclude;
        config.mouseHotkey.ProcessNames = ["notepad.exe"];

        ConfigManger.MigrateConfig(document.RootElement, config);

        Assert.AreEqual(config.CurrentConfigVersion + 1, config.ConfigVersion);
        Assert.AreEqual(HotKeyProcessScope.Exclude, config.mouseHotkey.ProcessScope);
        CollectionAssert.AreEqual(new[] { "notepad.exe" }, config.mouseHotkey.ProcessNames);
    }
    [TestMethod]
    public void LoadConfig_CallbackThrows_PreservesFileAndRemovesRegistration()
    {
        var key = "test-config-" + Guid.NewGuid().ToString("N");
        var path = Kitopia.Desktop.Features.Utils.KitopiaPaths.GetConfigFilePath(key);
        const string json = "{\"ConfigVersion\":0,\"Value\":42}";
        File.WriteAllText(path, json);
        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => ConfigManger.LoadConfig(key, new FailingConfig()));
            Assert.AreEqual(json, File.ReadAllText(path));
            Assert.IsFalse(ConfigManger.AllConfigs.ContainsKey(key));
        }
        finally { ConfigManger.RemoveConfig(key); File.Delete(path); }
    }

    [TestMethod]
    public void LoadConfig_InvalidJson_UsesBackupWithoutOverwritingEvidence()
    {
        var key = "test-config-" + Guid.NewGuid().ToString("N");
        var path = Kitopia.Desktop.Features.Utils.KitopiaPaths.GetConfigFilePath(key);
        File.WriteAllText(path, "broken");
        File.WriteAllText(path + ".bak", "{\"Value\":42}");
        try
        {
            var config = (SampleConfig)ConfigManger.LoadConfig(key, new SampleConfig());
            Assert.AreEqual(42, config.Value);
            Assert.IsTrue(config.Loaded);
            Assert.AreEqual("broken", File.ReadAllText(path));
            Assert.AreSame(config, new ConfigManger().Get<SampleConfig>());
            ConfigManger.Save(key);
            Assert.AreEqual(42, JsonSerializer.Deserialize<SampleConfig>(File.ReadAllText(path), ConfigManger.DefaultOptions)!.Value);
            Assert.AreEqual("{\"Value\":42}", File.ReadAllText(path + ".bak"));
        }
        finally
        {
            ConfigManger.RemoveConfig(key);
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }

    [TestMethod]
    public void LoadConfig_MissingMain_UsesBackupAndRestoresMain()
    {
        var key = "test-config-" + Guid.NewGuid().ToString("N");
        var path = KitopiaPaths.GetConfigFilePath(key);
        var previousServices = ServiceManager.Services;
        var toast = new RecordingToast();
        using var services = new ServiceCollection().AddSingleton<IToastService>(toast).BuildServiceProvider();
        ServiceManager.Services = services;
        const string backup = "{\"Value\":42}";
        File.WriteAllText(path + ".bak", backup);
        try
        {
            var config = (SampleConfig)ConfigManger.LoadConfig(key, new SampleConfig());
            Assert.AreEqual(42, config.Value);
            Assert.AreEqual(backup, File.ReadAllText(path));
            Assert.IsTrue(toast.Requests.Any(request => request.NotificationType == NotificationType.Warning
                && request.Text.Contains("备份")));
        }
        finally
        {
            ServiceManager.Services = previousServices;
            ConfigManger.RemoveConfig(key);
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }

    [TestMethod]
    public void LoadConfig_MissingMainAndBackup_ShowsDefaultCreationToast()
    {
        var key = "test-config-" + Guid.NewGuid().ToString("N");
        var path = KitopiaPaths.GetConfigFilePath(key);
        var previousServices = ServiceManager.Services;
        var toast = new RecordingToast();
        using var services = new ServiceCollection().AddSingleton<IToastService>(toast).BuildServiceProvider();
        ServiceManager.Services = services;

        try
        {
            var config = (SampleConfig)ConfigManger.LoadConfig(key, new SampleConfig { Value = 7 },
                useDefaultsOnInvalidJson: true);
            Assert.AreEqual(7, config.Value);
            Assert.IsTrue(File.Exists(path));
            Assert.IsTrue(toast.Requests.Any(request => request.NotificationType == NotificationType.Warning
                && request.Text.Contains("默认配置")));
        }
        finally
        {
            ServiceManager.Services = previousServices;
            ConfigManger.RemoveConfig(key);
            File.Delete(path);
        }
    }

    [TestMethod]
    public void RemoveConfig_PrefixOfAnotherPlugin_RemovesOnlyExactOwner()
    {
        var previous = ConfigManger.Configs;
        try
        {
            ConfigManger.Configs = new Dictionary<string, ConfigBase>
            {
                ["foo#Config"] = new SampleConfig(), ["foobar#Config"] = new SampleConfig()
            };
            ConfigManger.RemoveConfig("foo");
            Assert.IsFalse(ConfigManger.AllConfigs.ContainsKey("foo#Config"));
            Assert.IsTrue(ConfigManger.AllConfigs.ContainsKey("foobar#Config"));
            Assert.IsTrue(((IDictionary<string, ConfigBase>)ConfigManger.AllConfigs).IsReadOnly);
        }
        finally { ConfigManger.Configs = previous; }
    }

    [TestMethod]
    public void LoadConfig_MigrationThrows_DoesNotFallBackToOlderConfiguration()
    {
        var key = "test-config-" + Guid.NewGuid().ToString("N");
        var path = Kitopia.Desktop.Features.Utils.KitopiaPaths.GetConfigFilePath(key);
        const string json = "{\"Value\":42}";
        File.WriteAllText(path, json);
        File.WriteAllText(path + ".bak", "{\"Value\":1}");
        try
        {
            Assert.ThrowsExactly<JsonException>(() => ConfigManger.LoadConfig(key, new MigrationFailureConfig()));
            Assert.AreEqual(json, File.ReadAllText(path));
            Assert.IsFalse(ConfigManger.AllConfigs.ContainsKey(key));
        }
        finally { ConfigManger.RemoveConfig(key); File.Delete(path); File.Delete(path + ".bak"); }
    }

    [TestMethod]
    public void LoadConfig_RecoveryAllowedAndBothFilesInvalid_UsesDefaultsWithoutOverwriting()
    {
        var key = "test-config-" + Guid.NewGuid().ToString("N");
        var path = Kitopia.Desktop.Features.Utils.KitopiaPaths.GetConfigFilePath(key);
        var previousServices = ServiceManager.Services;
        var toast = new RecordingToast();
        using var services = new ServiceCollection().AddSingleton<IToastService>(toast).BuildServiceProvider();
        ServiceManager.Services = services;
        File.WriteAllText(path, "broken");
        const string invalidBackup = "{\"Value\":\"not an integer\"}";
        File.WriteAllText(path + ".bak", invalidBackup);
        try
        {
            var config = (SampleConfig)ConfigManger.LoadConfig(key, new SampleConfig { Value = 7 }, useDefaultsOnInvalidJson: true);
            Assert.AreEqual(7, config.Value);
            Assert.IsTrue(config.Loaded);
            Assert.AreEqual("broken", File.ReadAllText(path));
            Assert.AreEqual(invalidBackup, File.ReadAllText(path + ".bak"));
            ConfigManger.Save(key);
            Assert.AreEqual("broken", File.ReadAllText(path));
            Assert.AreEqual(invalidBackup, File.ReadAllText(path + ".bak"));
            Assert.IsTrue(toast.Requests.Any(request => request.NotificationType == NotificationType.Error
                && request.Text.Contains("备份")));
        }
        finally
        {
            ServiceManager.Services = previousServices;
            ConfigManger.RemoveConfig(key);
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }

    [TestMethod]
    public void LoadConfig_ValidMainAndDamagedBackup_ShowsToast()
    {
        var key = "test-config-" + Guid.NewGuid().ToString("N");
        var path = KitopiaPaths.GetConfigFilePath(key);
        var previousServices = ServiceManager.Services;
        var toast = new RecordingToast();
        using var services = new ServiceCollection().AddSingleton<IToastService>(toast).BuildServiceProvider();
        ServiceManager.Services = services;
        File.WriteAllText(path, "{\"Value\":42}");
        const string invalidBackup = "{\"Value\":\"not an integer\"}";
        File.WriteAllText(path + ".bak", invalidBackup);

        try
        {
            var config = (SampleConfig)ConfigManger.LoadConfig(key, new SampleConfig());
            Assert.AreEqual(42, config.Value);
            Assert.IsTrue(toast.Requests.Any(request => request.NotificationType == NotificationType.Error
                && request.Text.Contains("备份")));
            Assert.AreEqual(invalidBackup, File.ReadAllText(path + ".bak"));
        }
        finally
        {
            ServiceManager.Services = previousServices;
            ConfigManger.RemoveConfig(key);
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }

    [TestMethod]
    public void LoadConfig_UnreadableMainAndBackup_ShowsToastBeforeThrowing()
    {
        var key = "test-config-" + Guid.NewGuid().ToString("N");
        var path = KitopiaPaths.GetConfigFilePath(key);
        var previousServices = ServiceManager.Services;
        var toast = new RecordingToast();
        using var services = new ServiceCollection().AddSingleton<IToastService>(toast).BuildServiceProvider();
        ServiceManager.Services = services;
        File.WriteAllText(path, "broken");
        File.WriteAllText(path + ".bak", "broken backup");

        try
        {
            Assert.Throws<JsonException>(() => ConfigManger.LoadConfig(key, new SampleConfig()));
            Assert.IsTrue(toast.Requests.Any(request => request.NotificationType == NotificationType.Error
                && request.Text.Contains("备份")));
        }
        finally
        {
            ServiceManager.Services = previousServices;
            ConfigManger.RemoveConfig(key);
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }

    [TestMethod]
    public void Save_ConfigCallbackThrows_ShowsToast()
    {
        var key = "test-config-" + Guid.NewGuid().ToString("N");
        var previousServices = ServiceManager.Services;
        var toast = new RecordingToast();
        using var services = new ServiceCollection().AddSingleton<IToastService>(toast).BuildServiceProvider();
        ServiceManager.Services = services;
        ConfigManger.Configs.Add(key, new FailingSaveConfig());

        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => ConfigManger.Save(key));
            Assert.IsTrue(toast.Requests.Any(request => request.NotificationType == NotificationType.Error
                && request.Text.Contains("保存")));
        }
        finally
        {
            ServiceManager.Services = previousServices;
            ConfigManger.RemoveConfig(key);
        }
    }

    [TestMethod]
    public void MigrateLegacyDirectory_WhenTargetContainsFiles_CopiesMissingFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "kitopia-paths-" + Guid.NewGuid().ToString("N"));
        var legacy = Path.Combine(root, "legacy");
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(legacy, "old.json"), "old");
        File.WriteAllText(Path.Combine(legacy, "existing.json"), "legacy");
        File.WriteAllText(Path.Combine(target, "existing.json"), "current");

        try
        {
            KitopiaPaths.MigrateLegacyDirectory(legacy, target);
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(target, "old.json")));
            Assert.AreEqual("current", File.ReadAllText(Path.Combine(target, "existing.json")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    public sealed class MigrationFailureConfig : SampleConfig
    {
        public override void MigrateConfig(JsonElement root)
        {
            if (Value == 42) throw new JsonException("migration failure");
        }
    }

    public class SampleConfig : ConfigBase
    {
        public int Value;
        [System.Text.Json.Serialization.JsonIgnore] public bool Loaded;
        public override void AfterLoad() => Loaded = true;
    }

    public sealed class FailingConfig : SampleConfig
    {
        public override void AfterLoad() => throw new InvalidOperationException("hook failure");
    }

    public sealed class FailingSaveConfig : SampleConfig
    {
        public override void BeforeSave() => throw new InvalidOperationException("save failure");
    }

    private sealed class RecordingToast : IToastService
    {
        public List<ToastRequest> Requests { get; } = [];
        public void Init() { }
        public Task Show(string header, string text, NotificationType notificationType = NotificationType.Information,
            Window? dialogWindow = null) => Show(new ToastRequest { Header = header, Text = text, NotificationType = notificationType });
        public Task Show(ToastRequest request, Window? dialogWindow = null)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
        public IToastProgressHandle ShowProgress(string header, string text, NotificationType notificationType,
            double initialProgress = 0, bool isIndeterminate = false) => throw new NotSupportedException();
        public bool HasUnreadSuppressedNotifications() => false;
        public bool TryOpenLatestSuppressedNotification() => false;
        public bool ShowSuppressedNotificationCenter() => false;
        public void ClearUnreadSuppressedNotifications() { }
        public void Unregister() { }
    }

}
