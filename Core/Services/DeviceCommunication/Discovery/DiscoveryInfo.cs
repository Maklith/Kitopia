namespace Core.Services.DeviceCommunication.Discovery;

public sealed class DiscoveryInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Port { get; set; }
    public bool SupportsQuic { get; set; } = true;
}
