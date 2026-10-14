using System;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Feature.DeviceCommunication.Discovery;
using Kitopia.Feature.DeviceCommunication.Identity;

namespace Kitopia.Desktop.Services;

public sealed class DesktopDeviceIdentityStore : IDeviceIdentityStore
{
    private readonly IConfigService _configService;

    public DesktopDeviceIdentityStore(IConfigService configService)
    {
        _configService = configService;
    }

    public bool TryGetIdentity(out DeviceIdentity identity)
    {
        var privateKey = _configService.Config.devicePrivateKey?.Trim() ?? string.Empty;
        if (!DeviceDiscoverySignature.TryDerivePublicKey(privateKey, out var publicKey))
        {
            identity = default!;
            return false;
        }

        identity = new DeviceIdentity(
            publicKey,
            privateKey,
            DeviceDiscoverySignature.ComputePublicKeyHash(publicKey));
        return true;
    }

    public DeviceIdentity EnsureIdentity()
    {
        var changed = _configService.Config.EnsureDeviceIdentity();
        if (changed)
        {
            _configService.Save("KitopiaConfig");
        }

        if (!TryGetIdentity(out var identity))
        {
            throw new InvalidOperationException("Device identity is not initialized.");
        }

        return identity;
    }
}
