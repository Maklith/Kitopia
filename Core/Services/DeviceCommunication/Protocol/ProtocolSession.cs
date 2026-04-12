using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Text.Json;
using Core.Services.DeviceCommunication.Routing;

namespace Core.Services.DeviceCommunication.Protocol;

public sealed class ProtocolSession
{
    private static readonly byte[] FrameMagic = Encoding.ASCII.GetBytes("KDC1");
    private readonly IMessageRouter _router;

    public ProtocolSession(IMessageRouter router)
    {
        _router = router;
    }

    public async ValueTask HandleAsync(
        LocalDataTransportProtocol protocol,
        IPEndPoint remoteEndPoint,
        PipeReader payloadReader,
        CancellationToken cancellationToken = default)
    {
        var frameHeader = await LocalDataPipeIo.ReadExactlyOrEndAsync(payloadReader, 16, cancellationToken);
        if (frameHeader is null)
        {
            return;
        }

        if (!frameHeader.AsSpan(0, 4).SequenceEqual(FrameMagic))
        {
            throw new InvalidOperationException("Invalid protocol frame magic.");
        }

        var envelopeLength = BinaryPrimitives.ReadInt32LittleEndian(frameHeader.AsSpan(4, 4));
        var payloadLength = BinaryPrimitives.ReadInt64LittleEndian(frameHeader.AsSpan(8, 8));
        if (envelopeLength <= 0 || payloadLength < 0)
        {
            throw new InvalidOperationException("Invalid frame lengths.");
        }

        var envelopeBytes = await LocalDataPipeIo.ReadExactlyAsync(payloadReader, envelopeLength, cancellationToken);
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

        var payloadPipe = new Pipe();

        async Task ProducePayloadAsync()
        {
            Exception? writerError = null;
            try
            {
                var remaining = payloadLength;
                while (remaining > 0)
                {
                    var readResult = await payloadReader.ReadAsync(cancellationToken);
                    var buffer = readResult.Buffer;
                    if (buffer.Length == 0)
                    {
                        payloadReader.AdvanceTo(buffer.End);
                        if (readResult.IsCompleted)
                        {
                            throw new EndOfStreamException("Unexpected end of stream while reading payload.");
                        }

                        continue;
                    }

                    var toCopy = Math.Min((long)buffer.Length, remaining);
                    foreach (var segment in buffer.Slice(0, toCopy))
                    {
                        if (!segment.IsEmpty)
                        {
                            await payloadPipe.Writer.WriteAsync(segment, cancellationToken);
                        }
                    }

                    remaining -= toCopy;
                    payloadReader.AdvanceTo(buffer.GetPosition(toCopy), buffer.End);
                }
            }
            catch (Exception ex)
            {
                writerError = ex;
            }
            finally
            {
                await payloadPipe.Writer.CompleteAsync(writerError);
            }
        }

        var producerTask = ProducePayloadAsync();
        var context = new MessageContext(protocol, remoteEndPoint, string.Empty);
        Exception? consumerError = null;
        try
        {
            await _router.RouteAsync(context, envelope, payloadPipe.Reader, cancellationToken);
        }
        catch (Exception ex)
        {
            consumerError = ex;
            throw;
        }
        finally
        {
            await payloadPipe.Reader.CompleteAsync(consumerError);
        }

        await producerTask;
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
