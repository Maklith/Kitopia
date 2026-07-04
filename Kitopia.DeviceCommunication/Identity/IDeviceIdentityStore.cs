namespace Kitopia.DeviceCommunication.Identity;

public interface IDeviceIdentityStore
{
    bool TryGetIdentity(out DeviceIdentity identity);
    DeviceIdentity EnsureIdentity();
}
