using Kitopia.Feature.DeviceCommunication.Codecs;

namespace Kitopia.Feature.DeviceCommunication.Identity;

public sealed class DeviceIdentityProvider : IDeviceIdentityProvider
{
    private readonly IDeviceIdentityStore _deviceIdentityStore;

    public DeviceIdentityProvider(IDeviceIdentityStore deviceIdentityStore)
    {
        _deviceIdentityStore = deviceIdentityStore;
    }

    public string? GetLocalPublicKey()
    {
        return _deviceIdentityStore.TryGetIdentity(out var identity)
            ? identity.PublicKey
            : null;
    }
}
