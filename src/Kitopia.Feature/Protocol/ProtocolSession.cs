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
        string? authenticatedSenderId = null,
        CancellationToken cancellationToken = default,
        TimeSpan? envelopeTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(frameReader);
        if (envelopeTimeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(envelopeTimeout));
        }

        ProtocolFrameHeader header;
        DataEnvelope? envelope;
        using (var envelopeCancellation = envelopeTimeout.HasValue
                   ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                   : null)
        {
            if (envelopeTimeout is { } deadline)
            {
                envelopeCancellation!.CancelAfter(deadline);
            }

            var readToken = envelopeCancellation?.Token ?? cancellationToken;
            var frameHeader = await LocalDataPipeIo.ReadExactlyOrEndAsync(
                frameReader,
                ProtocolFrame.HeaderLength,
                readToken);
            if (frameHeader is null)
            {
                return false;
            }

            header = ProtocolFrame.ReadHeader(frameHeader);
            var envelopeBytes = await LocalDataPipeIo.ReadExactlyAsync(
                frameReader,
                header.EnvelopeLength,
                readToken);
            envelope = JsonSerializer.Deserialize(
                envelopeBytes,
                DeviceCommunicationJsonSerializerContext.Default.DataEnvelope);
            readToken.ThrowIfCancellationRequested();
        }

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.Route))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(authenticatedSenderId) &&
            (envelope.Metadata?.TryGetValue("senderId", out var senderId) != true ||
             !string.Equals(senderId, authenticatedSenderId, StringComparison.Ordinal)))
        {
            envelope = new DataEnvelope
            {
                Route = envelope.Route,
                Command = envelope.Command,
                StreamType = envelope.StreamType,
                ChannelId = envelope.ChannelId,
                Sequence = envelope.Sequence,
                ContentType = envelope.ContentType,
                Metadata = MergeMetadata(envelope.Metadata, authenticatedSenderId)
            };
        }

        var scopedPayloadReader = ProtocolFrame.CreatePayloadReader(frameReader, header.PayloadLength);
        await _dispatchAsync(envelope, scopedPayloadReader, cancellationToken);
        return true;
    }

    private static IReadOnlyDictionary<string, string?> MergeMetadata(
        IReadOnlyDictionary<string, string?>? metadata,
        string authenticatedSenderId)
    {
        var merged = metadata is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(metadata, StringComparer.Ordinal);
        merged["senderId"] = authenticatedSenderId;

        return merged;
    }
}
