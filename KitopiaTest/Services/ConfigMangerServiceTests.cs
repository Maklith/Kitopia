using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.Messaging;
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
    [DataRow(false)]
    [DataRow(true)]
    public void PrereleaseUpdates_LegacyAndConfiguredJson_DefaultsToDisabledAndPersistsSelection(bool allowPrereleaseUpdates)
    {
        Assert.IsFalse(new KitopiaConfig().allowPrereleaseUpdates);
        var config = JsonSerializer.Deserialize<KitopiaConfig>("{}", ConfigManger.DefaultOptions)!;
        Assert.IsFalse(config.allowPrereleaseUpdates);

        config.allowPrereleaseUpdates = allowPrereleaseUpdates;
        var json = JsonSerializer.Serialize(config, ConfigManger.DefaultOptions);
        var restored = JsonSerializer.Deserialize<KitopiaConfig>(json, ConfigManger.DefaultOptions)!;
        Assert.AreEqual(allowPrereleaseUpdates, restored.allowPrereleaseUpdates);
    }

    [TestMethod]
    public void ThemeColorConfig_LegacyAndCustomizedJson_PreservesDefaultsAndSelection()
    {
        var legacy = JsonSerializer.Deserialize<KitopiaConfig>("{}", ConfigManger.DefaultOptions)!;
        Assert.IsFalse(legacy.followSystemAccentColor);
        Assert.AreEqual("#0064FA", legacy.accentColor);

        legacy.followSystemAccentColor = true;
        legacy.accentColor = "#107C41";
        var json = JsonSerializer.Serialize(legacy, ConfigManger.DefaultOptions);
        var restored = JsonSerializer.Deserialize<KitopiaConfig>(json, ConfigManger.DefaultOptions)!;
        Assert.IsTrue(restored.followSystemAccentColor);
        Assert.AreEqual("#107C41", restored.accentColor);
    }

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

            Assert.AreSame(ConfigManger.Version, service.Version);
            Assert.AreEqual(ServiceManager.Version, service.Version.ToFullString());
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
    public void MigrateConfig_LegacySelectionTranslation_PreservesDisabledStateAndExclusions()
    {
        const string json = """
            {"ConfigVersion":2,"selectionTranslationEnabled":false,
             "selectionTranslationExcludedProcesses":[" WINWORD.EXE ","winword","code.exe",""],
             "selectionTranslationHotKey":{"IsEnabled":false,"SelectKey":65}}
            """;
        using var document = JsonDocument.Parse(json);
        var config = document.RootElement.Deserialize<KitopiaConfig>(ConfigManger.DefaultOptions)!;
        ConfigManger.MigrateConfig(document.RootElement, config);
        var model = config.selectionTranslationAutoHotKey;
        Assert.IsFalse(model.IsEnabled);
        Assert.AreEqual(HotKeyType.Mouse, model.Type);
        Assert.AreEqual(MouseHotKeyTrigger.DragRelease, model.MouseTrigger);
        Assert.AreEqual((ushort)4, model.DragDistancePixels);
        Assert.AreEqual(HotKeyProcessScope.Exclude, model.ProcessScope);
        CollectionAssert.AreEqual(new[] { "WINWORD", "code" }, model.ProcessNames);
        Assert.IsFalse(model.CanExecuteInProcess("winword"));
        Assert.IsFalse(config.selectionTranslationHotKey.IsEnabled);
        Assert.AreEqual(EKey.A, config.selectionTranslationHotKey.SelectKey);
        var saved = JsonSerializer.Serialize(config, ConfigManger.DefaultOptions);
        Assert.IsFalse(saved.Contains("selectionTranslationEnabled"));
        Assert.IsFalse(saved.Contains("selectionTranslationExcludedProcesses"));
        Assert.AreEqual(config.CurrentConfigVersion, config.ConfigVersion);
    }

    [TestMethod]
    public void MigrateConfig_ExplicitSelectionHotkey_PreservesConfiguredScopeAndTrigger()
    {
        const string json = """
            {"ConfigVersion":2,"selectionTranslationEnabled":false,
             "selectionTranslationExcludedProcesses":["winword"],
             "selectionTranslationAutoHotKey":{"IsEnabled":true,"Type":1,"MouseButton":2,
                "MouseTrigger":1,"DragDistancePixels":12,"ProcessScope":1,"ProcessNames":["code"]}}
            """;
        using var document = JsonDocument.Parse(json);
        var config = document.RootElement.Deserialize<KitopiaConfig>(ConfigManger.DefaultOptions)!;
        ConfigManger.MigrateConfig(document.RootElement, config);
        var model = config.selectionTranslationAutoHotKey;
        Assert.IsTrue(model.IsEnabled);
        Assert.AreEqual((ushort)2, model.MouseButton);
        Assert.AreEqual(MouseHotKeyTrigger.DragRelease, model.MouseTrigger);
        Assert.AreEqual((ushort)12, model.DragDistancePixels);
        Assert.AreEqual(HotKeyProcessScope.Include, model.ProcessScope);
        CollectionAssert.AreEqual(new[] { "code" }, model.ProcessNames);
        ConfigManger.MigrateConfig(document.RootElement, config);
        Assert.IsTrue(model.IsEnabled);
        CollectionAssert.AreEqual(new[] { "code" }, model.ProcessNames);
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
    [DataRow(false)]
    [DataRow(true)]
    public async Task LoadConfig_InvalidJson_UsesBackupWithoutOverwritingEvidence(bool saveAsync)
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
            if (saveAsync) await ConfigManger.SaveAsync(key);
            else ConfigManger.Save(key);
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
        var recipient = new object();
        var notifications = 0;
        WeakReferenceMessenger.Default.Register<string, string>(recipient, "ConfigSave", (_, _) => notifications++);

        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => ConfigManger.Save(key));
            Assert.AreEqual(0, notifications);
            Assert.IsTrue(toast.Requests.Any(request => request.NotificationType == NotificationType.Error
                && request.Text.Contains("保存")));
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
            ServiceManager.Services = previousServices;
            ConfigManger.RemoveConfig(key);
        }
    }

    [TestMethod]
    public void Save_UnchangedConfig_PreservesBackupAndDoesNotNotify()
    {
        var key = "test-config-" + Guid.NewGuid().ToString("N");
        var path = KitopiaPaths.GetConfigFilePath(key);
        var config = new SampleConfig { Name = key, Value = 7 };
        ConfigManger.Configs.Add(key, config);
        var recipient = new object();
        var notifications = 0;
        WeakReferenceMessenger.Default.Register<string, string>(recipient, "ConfigSave", (_, _) => notifications++);
        try
        {
            ConfigManger.Save(key);
            var first = File.ReadAllText(path);
            config.Value = 8;
            ConfigManger.Save(key);
            Assert.AreEqual(first, File.ReadAllText(path + ".bak"));
            ConfigManger.Save(key);
            Assert.AreEqual(first, File.ReadAllText(path + ".bak"));
            Assert.AreEqual(2, notifications);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
            ConfigManger.RemoveConfig(key);
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }

    [TestMethod]
    public async Task Save_ProtectedConfigurations_PreserveFilesAndDoNotNotify()
    {
        var originalConfigs = ConfigManger.Configs;
        ConfigManger.Configs = new Dictionary<string, ConfigBase>();
        var key = "test-config-" + Guid.NewGuid().ToString("N");
        var path = KitopiaPaths.GetConfigFilePath(key);
        var recipient = new object();
        var notifications = 0;
        WeakReferenceMessenger.Default.Register<string, string>(recipient, "ConfigSave", (_, _) => notifications++);
        try
        {
            const string future = "{\"ConfigVersion\":1,\"Value\":42}";
            File.WriteAllText(path, future);
            ConfigManger.LoadConfig(key, new SampleConfig());
            ConfigManger.Save(key);
            await ConfigManger.SaveAsync(key);
            ConfigManger.Save();
            Assert.AreEqual(future, File.ReadAllText(path));
            Assert.AreEqual(0, notifications);

            ConfigManger.RemoveConfig(key);
            File.WriteAllText(path, "broken");
            ConfigManger.LoadConfig(key, new SampleConfig(), useDefaultsOnInvalidJson: true);
            ConfigManger.Save(key);
            await ConfigManger.SaveAsync(key);
            ConfigManger.Save();
            Assert.AreEqual("broken", File.ReadAllText(path));
            Assert.IsFalse(File.Exists(path + ".bak"));
            Assert.AreEqual(0, notifications);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
            ConfigManger.RemoveConfig(key);
            ConfigManger.Configs = originalConfigs;
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }

    [TestMethod]
    public async Task SaveAsync_MutationAfterRequest_PersistsCapturedValue()
    {
        var key = "test-config-" + Guid.NewGuid().ToString("N");
        var path = KitopiaPaths.GetConfigFilePath(key);
        var config = new CapturingSaveConfig { Name = key, Value = 42 };
        ConfigManger.Configs.Add(key, config);
        try
        {
            var callerThread = Environment.CurrentManagedThreadId;
            var save = ConfigManger.SaveAsync(key);
            Assert.AreEqual(callerThread, config.BeforeSaveThread);
            config.Value = 99;
            await save;
            Assert.AreEqual(42, JsonSerializer.Deserialize<SampleConfig>(File.ReadAllText(path), ConfigManger.DefaultOptions)!.Value);
            Assert.AreEqual(99, config.Value);
        }
        finally
        {
            ConfigManger.RemoveConfig(key);
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }

    [TestMethod]
    public async Task SaveAsync_MultipleRequestsThenSynchronousFlush_NeverOverwritesLatestValue()
    {
        var key = "test-config-" + Guid.NewGuid().ToString("N");
        var path = KitopiaPaths.GetConfigFilePath(key);
        var config = new SampleConfig { Name = key };
        ConfigManger.Configs.Add(key, config);
        var requests = new List<Task>();
        try
        {
            for (var value = 0; value < 40; value++)
            {
                config.Value = value;
                requests.Add(ConfigManger.SaveAsync(key));
            }
            config.Value = 100;
            ConfigManger.Save(key);
            await Task.WhenAll(requests);
            Assert.AreEqual(100, JsonSerializer.Deserialize<SampleConfig>(File.ReadAllText(path), ConfigManger.DefaultOptions)!.Value);
            Assert.IsEmpty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp"));
        }
        finally
        {
            await Task.WhenAll(requests);
            ConfigManger.RemoveConfig(key);
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }

    [TestMethod]
    public void Save_AllConfigsWithOneFailure_SavesOthersAndReportsFailure()
    {
        var originalConfigs = ConfigManger.Configs;
        var prefix = "test-config-" + Guid.NewGuid().ToString("N");
        var failedKey = prefix + "-failed";
        var savedKey = prefix + "-saved";
        var path = KitopiaPaths.GetConfigFilePath(savedKey);
        ConfigManger.Configs = new Dictionary<string, ConfigBase>
        {
            [failedKey] = new FailingSaveConfig { Name = failedKey },
            [savedKey] = new SampleConfig { Name = savedKey, Value = 42 }
        };
        var recipient = new object();
        var notifications = 0;
        WeakReferenceMessenger.Default.Register<string, string>(recipient, "ConfigSave", (_, _) => notifications++);
        try
        {
            var exception = Assert.ThrowsExactly<AggregateException>(ConfigManger.Save);
            Assert.AreEqual(1, exception.InnerExceptions.Count);
            Assert.AreEqual(42, JsonSerializer.Deserialize<SampleConfig>(File.ReadAllText(path), ConfigManger.DefaultOptions)!.Value);
            Assert.AreEqual(1, notifications);
            Assert.IsFalse(File.Exists(KitopiaPaths.GetConfigFilePath(failedKey)));
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
            ConfigManger.RemoveConfig(savedKey);
            ConfigManger.RemoveConfig(failedKey);
            ConfigManger.Configs = originalConfigs;
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LoadAndSave_LegacyConfigWithMissingNewFields_PreservesExistingSettings()
    {
        var key = "test-config-" + Guid.NewGuid().ToString("N");
        var path = KitopiaPaths.GetConfigFilePath(key);
        File.WriteAllText(path, "{\"ConfigVersion\":2,\"maxHistory\":17,\"themeChoice\":1,\"deviceBroadcastName\":\"Existing\"}");
        try
        {
            var config = (KitopiaConfig)ConfigManger.LoadConfig(key, new KitopiaConfig());
            Assert.AreEqual(string.Empty, config.language);
            Assert.AreEqual(50, config.indexingMaximumCpuUsagePercent);
            ConfigManger.Save(key);
            var restored = JsonSerializer.Deserialize<KitopiaConfig>(File.ReadAllText(path), ConfigManger.DefaultOptions)!;
            Assert.AreEqual(17, restored.maxHistory);
            Assert.AreEqual(ThemeEnum.深色, restored.themeChoice);
            Assert.AreEqual("Existing", restored.deviceBroadcastName);
        }
        finally
        {
            ConfigManger.RemoveConfig(key);
            File.Delete(path);
            File.Delete(path + ".bak");
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

    public sealed class CapturingSaveConfig : SampleConfig
    {
        [System.Text.Json.Serialization.JsonIgnore] public int BeforeSaveThread;
        public override void BeforeSave() => BeforeSaveThread = Environment.CurrentManagedThreadId;
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
