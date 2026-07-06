using Kitopia.DeviceCommunication.Discovery;

namespace Kitopia.Mobile.Services;

public sealed class MobileDeviceCommunicationSettings : IDeviceCommunicationSettings
{
    private readonly MobileConfigService _config;

    public MobileDeviceCommunicationSettings(MobileConfigService config)
    {
        _config = config;
    }

    public string BroadcastName { get; } = $"{Environment.MachineName} Mobile";

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
