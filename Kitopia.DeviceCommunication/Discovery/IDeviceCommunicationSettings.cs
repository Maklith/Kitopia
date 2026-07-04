namespace Kitopia.DeviceCommunication.Discovery;

public interface IDeviceCommunicationSettings
{
    string BroadcastName { get; }
    string? GetCustomName(string publicKey);
}
