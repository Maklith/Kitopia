namespace Kitopia.DeviceCommunication.Discovery;

public sealed class DiscoveryInfo
{
    public string MessageType { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int TcpPort { get; set; }
    public long TimestampUnixSeconds { get; set; }
    public string Signature { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string Nonce { get; set; } = string.Empty;
}
