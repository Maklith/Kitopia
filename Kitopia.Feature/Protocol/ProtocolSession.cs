using System.IO.Pipelines;
using System.Text.Json;
using Kitopia.Feature.DeviceCommunication.Serialization;
using Kitopia.Feature.DeviceCommunication.Transport;

namespace Kitopia.Feature.DeviceCommunication.Protocol;

public sealed class ProtocolSession
{
    private readonly Func<DataEnvelope, PipeReader, CancellationToken, ValueTask> _dispatchAsync;

    public ProtocolSession(Func<DataEnvelope, PipeReader, CancellationToken, ValueTask> dispatchAsync)
    {
        _dispatchAsync = dispatchAsync ?? throw new ArgumentNullException(nameof(dispatchAsync));
    }

    public async ValueTask<bool> HandleAsync(
        PipeReader frameReader,
        string? senderIdFallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frameReader);

        var frameHeader = await LocalDataPipeIo.ReadExactlyOrEndAsync(
            frameReader,
            ProtocolFrame.HeaderLength,
            cancellationToken);
        if (frameHeader is null)
        {
            return false;
        }

        var header = ProtocolFrame.ReadHeader(frameHeader);
        var envelopeBytes = await LocalDataPipeIo.ReadExactlyAsync(
            frameReader,
            header.EnvelopeLength,
            cancellationToken);
        var envelope = JsonSerializer.Deserialize(
            envelopeBytes,
            DeviceCommunicationJsonSerializerContext.Default.DataEnvelope);
        if (envelope is null || string.IsNullOrWhiteSpace(envelope.Route))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(senderIdFallback) &&
            string.IsNullOrWhiteSpace(envelope.Metadata?.TryGetValue("senderId", out var senderId) == true
                ? senderId
                : null))
        {
            envelope = new DataEnvelope
            {
                Route = envelope.Route,
                Command = envelope.Command,
                StreamType = envelope.StreamType,
                ChannelId = envelope.ChannelId,
                Sequence = envelope.Sequence,
                ContentType = envelope.ContentType,
                Metadata = MergeMetadata(envelope.Metadata, senderIdFallback)
            };
        }

        var scopedPayloadReader = ProtocolFrame.CreatePayloadReader(frameReader, header.PayloadLength);
        await _dispatchAsync(envelope, scopedPayloadReader, cancellationToken);
        return true;
    }

    private static IReadOnlyDictionary<string, string?> MergeMetadata(
        IReadOnlyDictionary<string, string?>? metadata,
        string senderIdFallback)
    {
        var merged = metadata is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(metadata, StringComparer.Ordinal);
        if (!merged.ContainsKey("senderId"))
        {
            merged["senderId"] = senderIdFallback;
        }

        return merged;
    }
}
