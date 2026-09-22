using System.Text.Json;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
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
            Assert.AreSame(ConfigManger.Configs, service.Configs);
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

            Assert.AreEqual(ConfigManger.CurrentConfigVersion, config.ConfigVersion);
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
        var config = new KitopiaConfig { ConfigVersion = ConfigManger.CurrentConfigVersion };
        config.mouseHotkey.ProcessScope = HotKeyProcessScope.Exclude;
        config.mouseHotkey.ProcessNames = ["notepad.exe"];
        config.mouseHotkey.IgnoreTextInput = false;

        ConfigManger.MigrateConfig(document.RootElement, config);

        Assert.AreEqual(HotKeyProcessScope.Exclude, config.mouseHotkey.ProcessScope);
        CollectionAssert.AreEqual(new[] { "notepad.exe" }, config.mouseHotkey.ProcessNames);
        Assert.IsFalse(config.mouseHotkey.IgnoreTextInput);
    }
}
