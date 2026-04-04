using PluginCore;

namespace Core.Services.DeviceCommunication;

public enum DeviceChatDirection
{
    Incoming = 0,
    Outgoing = 1,
    System = 2
}

public enum DeviceChatEntryType
{
    Text = 0,
    File = 1,
    FileRequest = 2,
    TransferStatus = 3
}

public sealed class DeviceChatMessage
{
    public long Id { get; init; }
    public string PeerKey { get; init; } = string.Empty;
    public string PeerId { get; init; } = string.Empty;
    public string PeerName { get; init; } = string.Empty;
    public string PeerAddress { get; init; } = string.Empty;
    public int PeerPort { get; init; }
    public DeviceChatDirection Direction { get; init; } = DeviceChatDirection.System;
    public DeviceChatEntryType EntryType { get; init; } = DeviceChatEntryType.Text;
    public string Content { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public string RequestId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}

public sealed class DeviceChatConversation
{
    public string PeerKey { get; init; } = string.Empty;
    public string PeerId { get; init; } = string.Empty;
    public string PeerName { get; init; } = string.Empty;
    public string PeerAddress { get; init; } = string.Empty;
    public int PeerPort { get; init; }
    public long LastMessageId { get; init; }
    public DeviceChatDirection LastDirection { get; init; } = DeviceChatDirection.System;
    public DeviceChatEntryType LastEntryType { get; init; } = DeviceChatEntryType.Text;
    public string LastContent { get; init; } = string.Empty;
    public string LastFileName { get; init; } = string.Empty;
    public string LastStatus { get; init; } = string.Empty;
    public DateTime LastTimestampUtc { get; init; } = DateTime.UtcNow;
}

public static class DeviceChatPeerKey
{
    public static string Build(DeviceModel? device)
    {
        if (device is null)
        {
            return Build(string.Empty, string.Empty, 0);
        }

        return Build(device.Id, device.Address?.ToString() ?? string.Empty, device.Port);
    }

    public static string Build(string? peerId, string? peerAddress, int peerPort)
    {
        if (!string.IsNullOrWhiteSpace(peerId))
        {
            return $"id:{peerId.Trim()}";
        }

        var normalizedAddress = string.IsNullOrWhiteSpace(peerAddress)
            ? "unknown"
            : peerAddress.Trim().ToLowerInvariant();
        var normalizedPort = peerPort > 0 ? peerPort : 0;
        return $"ep:{normalizedAddress}:{normalizedPort}";
    }

    public static string ResolveDisplayName(string? peerName, string? peerAddress)
    {
        if (!string.IsNullOrWhiteSpace(peerName))
        {
            return peerName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(peerAddress))
        {
            return peerAddress.Trim();
        }

        return "Unknown Device";
    }
}
