using System.IO.Pipelines;
using System.Net;
using System.Text.Json;
using Core.Services.DeviceCommunication.Routing;
using SharedProtocolFrame = Kitopia.DeviceCommunication.Protocol.ProtocolFrame;
using SharedProtocolFrameHeader = Kitopia.DeviceCommunication.Protocol.ProtocolFrameHeader;
using SharedLocalDataPipeIo = Kitopia.DeviceCommunication.Transport.LocalDataPipeIo;

namespace Core.Services.DeviceCommunication.Protocol;

public sealed class ProtocolSession
{
    private readonly DeviceMessageDispatcher _dispatcher;

    public ProtocolSession(DeviceMessageDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async ValueTask HandleAsync(
        LocalDataTransportProtocol protocol,
        IPEndPoint remoteEndPoint,
        PipeReader payloadReader,
        CancellationToken cancellationToken = default)
    {
        var frameHeader = await SharedLocalDataPipeIo.ReadExactlyOrEndAsync(
            payloadReader,
            SharedProtocolFrame.HeaderLength,
            cancellationToken);
        if (frameHeader is null)
        {
            return;
        }

        var header = ReadCoreFrameHeader(frameHeader);
        var envelopeBytes = await SharedLocalDataPipeIo.ReadExactlyAsync(
            payloadReader,
            header.EnvelopeLength,
            cancellationToken);
        var envelope = JsonSerializer.Deserialize<DataEnvelope>(envelopeBytes);
        if (envelope is null || string.IsNullOrWhiteSpace(envelope.Route))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(envelope.Metadata?.TryGetValue("senderId", out var senderId) == true ? senderId : null))
        {
            envelope = new DataEnvelope
            {
                Route = envelope.Route,
                Command = envelope.Command,
                StreamType = envelope.StreamType,
                ChannelId = envelope.ChannelId,
                Sequence = envelope.Sequence,
                ContentType = envelope.ContentType,
                Metadata = MergeMetadata(envelope.Metadata, remoteEndPoint.Address.ToString())
            };
        }

        var scopedPayloadReader = SharedProtocolFrame.CreatePayloadReader(payloadReader, header.PayloadLength);
        var context = new MessageContext(protocol, remoteEndPoint, string.Empty);
        await _dispatcher.DispatchAsync(context, envelope, scopedPayloadReader, cancellationToken);
    }

    private static SharedProtocolFrameHeader ReadCoreFrameHeader(byte[] frameHeader)
    {
        try
        {
            return SharedProtocolFrame.ReadHeader(frameHeader);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
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