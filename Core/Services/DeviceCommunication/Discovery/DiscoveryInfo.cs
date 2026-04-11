namespace Core.Services.DeviceCommunication.Discovery;

public sealed class DiscoveryInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int UdpPort { get; set; }
    public int QuicPort { get; set; }
    public bool SupportsQuic { get; set; }
}
