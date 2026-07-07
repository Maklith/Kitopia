namespace Kitopia.DeviceCommunication.Discovery;

public interface IDeviceCommunicationSettings
{
    string BroadcastName { get; }
    string OperatingSystemName => DeviceOperatingSystemName.ResolveCurrent();
    string? GetCustomName(string publicKey);
    void SetCustomName(string publicKey, string name);
    void RemoveCustomName(string publicKey);
}
