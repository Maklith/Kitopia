using Core.Services.Config;
using Kitopia.DeviceCommunication.Discovery;

namespace Core.Services.DeviceCommunication;

public sealed class DesktopDeviceCommunicationSettings : IDeviceCommunicationSettings
{
    public string BroadcastName => ConfigManger.Config.deviceBroadcastName;

    public string? GetCustomName(string publicKey)
    {
        return ConfigManger.Config.deviceCustomNames.TryGetValue(publicKey, out var customName)
            ? customName
            : null;
    }
}
