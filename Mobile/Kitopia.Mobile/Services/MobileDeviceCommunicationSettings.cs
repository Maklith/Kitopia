using Kitopia.Feature.DeviceCommunication.Discovery;

namespace Kitopia.Mobile.Services;

public sealed class MobileDeviceCommunicationSettings : IDeviceCommunicationSettings
{
    private readonly MobileConfigService _config;
    private readonly IMobilePlatformRuntimeFeatures _platformFeatures;

    public MobileDeviceCommunicationSettings(
        MobileConfigService config,
        IMobilePlatformRuntimeFeatures platformFeatures)
    {
        _config = config;
        _platformFeatures = platformFeatures;
    }

    public string BroadcastName => string.IsNullOrWhiteSpace(_platformFeatures.DefaultDeviceName)
        ? DefaultMobilePlatformRuntimeFeatures.Instance.DefaultDeviceName
        : _platformFeatures.DefaultDeviceName.Trim();

    public string OperatingSystemName => _platformFeatures.OperatingSystemName;

    public string? GetCustomName(string publicKey)
    {
        return _config.GetCustomName(publicKey);
    }

    public void SetCustomName(string publicKey, string name)
    {
        _config.SetCustomName(publicKey, name);
    }

    public void RemoveCustomName(string publicKey)
    {
        _config.RemoveCustomName(publicKey);
    }
}
