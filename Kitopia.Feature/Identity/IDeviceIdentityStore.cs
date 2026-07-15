namespace Kitopia.Feature.DeviceCommunication.Identity;

public interface IDeviceIdentityStore
{
    bool TryGetIdentity(out DeviceIdentity identity);
    DeviceIdentity EnsureIdentity();
}
