using System.Net;

namespace Core.Services.DeviceCommunication;

public sealed class LocalDataBusEnvelope
{
    public string Route { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string? ChannelId { get; set; }
    public string? ContentType { get; set; }
    public string? FileName { get; set; }
    public string? Message { get; set; }
    public Dictionary<string, string?>? Metadata { get; set; }

    public LocalDataBusEnvelope CreateSendSnapshot()
    {
        var route = Route?.Trim();
        if (string.IsNullOrWhiteSpace(route))
        {
            throw new ArgumentException("Route is required.", nameof(Route));
        }

        var command = Command?.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("Command is required.", nameof(Command));
        }

        string? normalizedChannelId = null;
        if (!string.IsNullOrWhiteSpace(ChannelId))
        {
            if (!Guid.TryParse(ChannelId, out var parsedChannelId))
            {
                throw new ArgumentException("ChannelId must be a valid guid.", nameof(ChannelId));
            }

            if (parsedChannelId != Guid.Empty)
            {
                normalizedChannelId = parsedChannelId.ToString("D");
            }
        }

        return new LocalDataBusEnvelope
        {
            Route = route.ToLowerInvariant(),
            Command = command,
            ChannelId = normalizedChannelId,
            ContentType = string.IsNullOrWhiteSpace(ContentType) ? null : ContentType.Trim(),
            FileName = string.IsNullOrWhiteSpace(FileName) ? null : FileName.Trim(),
            Message = Message,
            Metadata = Metadata is null ? null : new Dictionary<string, string?>(Metadata)
        };
    }
}

public sealed class LocalDataBusEnvelopeReceivedEventArgs : EventArgs
{
    public LocalDataBusEnvelopeReceivedEventArgs(
        LocalDataTransportProtocol protocol,
        IPEndPoint remoteEndPoint,
        Guid channelId,
        LocalDataBusEnvelope envelope,
        DateTimeOffset timestampUtc)
    {
        Protocol = protocol;
        RemoteEndPoint = remoteEndPoint;
        ChannelId = channelId;
        Envelope = envelope;
        TimestampUtc = timestampUtc;
    }

    public LocalDataTransportProtocol Protocol { get; }
    public IPEndPoint RemoteEndPoint { get; }
    public Guid ChannelId { get; }
    public LocalDataBusEnvelope Envelope { get; }
    public DateTimeOffset TimestampUtc { get; }
}
