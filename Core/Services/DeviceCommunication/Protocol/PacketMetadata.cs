namespace Core.Services.DeviceCommunication.Protocol;

public class PacketMetadata
{
    public string Type { get; set; } = string.Empty;
    public string Meta { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public long Size { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public bool Accepted { get; set; }
    public int SenderPort { get; set; }
    public string SenderId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
}
