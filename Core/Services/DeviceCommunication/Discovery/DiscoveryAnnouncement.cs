namespace Core.Services.DeviceCommunication.Discovery;

public sealed class DiscoveryAnnouncement
{
    public string DeviceId { get; init; } = string.Empty;
    public string DeviceName { get; init; } = string.Empty;
    public int Port { get; init; }
    public bool SupportsQuic { get; init; }
}
