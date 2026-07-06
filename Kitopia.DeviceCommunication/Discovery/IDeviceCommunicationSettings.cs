namespace Kitopia.DeviceCommunication.Discovery;

public interface IDeviceCommunicationSettings
{
    string BroadcastName { get; }
    string? GetCustomName(string publicKey);
    void SetCustomName(string publicKey, string name);
    void RemoveCustomName(string publicKey);
}
