namespace Kitopia.Feature.DeviceCommunication.Discovery;

public interface IDeviceCommunicationSettings
{
    string BroadcastName { get; }
    string OperatingSystemName => "Unknown";
    bool EnableLegacyDeviceCompatibility => false;
    bool CanEnableLegacyDeviceCompatibility => false;
    void EnableLegacyCompatibility() => throw new NotSupportedException();
    string? GetCustomName(string publicKey);
    void SetCustomName(string publicKey, string name);
    void RemoveCustomName(string publicKey);
}
