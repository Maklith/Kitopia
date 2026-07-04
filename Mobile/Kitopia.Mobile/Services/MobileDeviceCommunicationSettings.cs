using Kitopia.DeviceCommunication.Discovery;

namespace Kitopia.Mobile.Services;

public sealed class MobileDeviceCommunicationSettings : IDeviceCommunicationSettings
{
    public string BroadcastName { get; } = $"{Environment.MachineName} Mobile";

    public string? GetCustomName(string publicKey)
    {
        _ = publicKey;
        return null;
    }
}
