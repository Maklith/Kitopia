using Core.Services.Config;
using Kitopia.DeviceCommunication.Identity;

namespace Core.Services.DeviceCommunication;

public sealed class DesktopDeviceIdentityStore : IDeviceIdentityStore
{
    public bool TryGetIdentity(out DeviceIdentity identity)
    {
        var privateKey = ConfigManger.Config.devicePrivateKey?.Trim() ?? string.Empty;
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
        var changed = ConfigManger.Config.EnsureDeviceIdentity();
        if (changed)
        {
            ConfigManger.Save("KitopiaConfig");
        }

        if (!TryGetIdentity(out var identity))
        {
            throw new InvalidOperationException("Device identity is not initialized.");
        }

        return identity;
    }
}
