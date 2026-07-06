using Core.Services.Config;
using Kitopia.DeviceCommunication.Discovery;

namespace Core.Services.DeviceCommunication;

public sealed class DesktopDeviceCommunicationSettings : IDeviceCommunicationSettings
{
    public string BroadcastName => ConfigManger.Config.deviceBroadcastName;

    public string? GetCustomName(string publicKey)
    {
        ConfigManger.Config.deviceCustomNames ??= new();
        return ConfigManger.Config.deviceCustomNames.TryGetValue(publicKey, out var name) ? name : null;
    }

    public void SetCustomName(string publicKey, string name)
    {
        ConfigManger.Config.deviceCustomNames ??= new();
        ConfigManger.Config.deviceCustomNames[publicKey] = name;
        ConfigManger.Save("KitopiaConfig");
    }

    public void RemoveCustomName(string publicKey)
    {
        ConfigManger.Config.deviceCustomNames ??= new();
        ConfigManger.Config.deviceCustomNames.Remove(publicKey);
        ConfigManger.Save("KitopiaConfig");
    }
}
