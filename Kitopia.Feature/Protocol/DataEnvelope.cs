using Kitopia.Feature.DeviceCommunication.Routing;

namespace Kitopia.Feature.DeviceCommunication.Protocol;

public sealed class DataEnvelope
{
    public string Route { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public DataStreamType StreamType { get; init; }
    public Guid ChannelId { get; init; }
    public long Sequence { get; init; }
    public string? ContentType { get; init; }
    public IReadOnlyDictionary<string, string?>? Metadata { get; init; }
}