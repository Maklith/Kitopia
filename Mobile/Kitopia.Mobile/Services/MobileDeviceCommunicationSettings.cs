using Core.Services.Interfaces;
using Kitopia.DeviceCommunication.Discovery;

namespace Kitopia.Mobile.Services;

public sealed class MobileDeviceCommunicationSettings : IDeviceCommunicationSettings
{
    private readonly IConfigService _config;

    public MobileDeviceCommunicationSettings(IConfigService config)
    {
        _config = config;
    }

    public string BroadcastName { get; } = $"{Environment.MachineName} Mobile";

    public string? GetCustomName(string publicKey)
    {
        return _config.GetDeviceCustomName(publicKey);
    }
}
