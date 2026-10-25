using Kitopia.Desktop.Abstractions;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Services;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class DesktopDeviceCommunicationSettingsTests
{
    [TestMethod]
    public void EnableLegacyCompatibility_UpdatesNotifiesAndSavesOnce()
    {
        var configService = new RecordingConfigService();
        var settings = new DesktopDeviceCommunicationSettings(configService, new PlatformInfo());
        var changes = new List<string>();
        configService.Config.ConfigChanged += (_, args) => changes.Add(args.Name);

        settings.EnableLegacyCompatibility();
        settings.EnableLegacyCompatibility();

        Assert.IsTrue(settings.CanEnableLegacyDeviceCompatibility);
        Assert.IsTrue(settings.EnableLegacyDeviceCompatibility);
        Assert.IsTrue(configService.Config.enableLegacyDeviceCompatibility);
        CollectionAssert.AreEqual(new[] { nameof(KitopiaConfig.enableLegacyDeviceCompatibility) }, changes);
        CollectionAssert.AreEqual(new[] { "KitopiaConfig" }, configService.SavedKeys);
    }

    private sealed class RecordingConfigService : IConfigService
    {
        public KitopiaConfig Config { get; } = new();
        public List<string> SavedKeys { get; } = [];
        public void Save(string key) => SavedKeys.Add(key);
    }

    private sealed class PlatformInfo : IDesktopPlatformInfo
    {
        public string OperatingSystemName => "Windows";
    }
}
