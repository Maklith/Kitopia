using Core.Services.Config;
using Core.Services.Interfaces;
using Kitopia.DeviceCommunication.Discovery;

namespace Core.Services.DeviceCommunication;

public sealed class DesktopDeviceCommunicationSettings : IDeviceCommunicationSettings
{
    private readonly IConfigService _config;

    public DesktopDeviceCommunicationSettings(IConfigService config)
    {
        _config = config;
    }

    public string BroadcastName => ConfigManger.Config.deviceBroadcastName;

    public string? GetCustomName(string publicKey)
    {
        return _config.GetDeviceCustomName(publicKey);
    }
}
