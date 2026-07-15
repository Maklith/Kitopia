using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Abstractions;
using Kitopia.Feature.DeviceCommunication.Discovery;

namespace Kitopia.Desktop.Services;

public sealed class DesktopDeviceCommunicationSettings : IDeviceCommunicationSettings
{
    private readonly IConfigService _configService;
    private readonly IDesktopPlatformInfo _desktopPlatformInfo;

    public DesktopDeviceCommunicationSettings(
        IConfigService configService,
        IDesktopPlatformInfo desktopPlatformInfo)
    {
        _configService = configService;
        _desktopPlatformInfo = desktopPlatformInfo;
    }

    public string BroadcastName => _configService.Config.deviceBroadcastName;

    public string OperatingSystemName => _desktopPlatformInfo.OperatingSystemName;

    public string? GetCustomName(string publicKey)
    {
        _configService.Config.deviceCustomNames ??= new();
        return _configService.Config.deviceCustomNames.TryGetValue(publicKey, out var name) ? name : null;
    }

    public void SetCustomName(string publicKey, string name)
    {
        _configService.Config.deviceCustomNames ??= new();
        _configService.Config.deviceCustomNames[publicKey] = name;
        _configService.Save("KitopiaConfig");
    }

    public void RemoveCustomName(string publicKey)
    {
        _configService.Config.deviceCustomNames ??= new();
        _configService.Config.deviceCustomNames.Remove(publicKey);
        _configService.Save("KitopiaConfig");
    }
}
