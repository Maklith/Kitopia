using Core.Services.Interfaces;

namespace Core.Services.Config;

public sealed class DesktopConfigService : IConfigService
{
    public string? GetDeviceCustomName(string deviceId)
    {
        ConfigManger.Config.deviceCustomNames ??= new();
        return ConfigManger.Config.deviceCustomNames.TryGetValue(deviceId, out var name) ? name : null;
    }

    public void SetDeviceCustomName(string deviceId, string name)
    {
        ConfigManger.Config.deviceCustomNames[deviceId] = name;
        ConfigManger.Save("KitopiaConfig");
    }

    public void RemoveDeviceCustomName(string deviceId)
    {
        ConfigManger.Config.deviceCustomNames.Remove(deviceId);
        ConfigManger.Save("KitopiaConfig");
    }
}
