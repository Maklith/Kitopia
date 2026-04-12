using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using Core.Services.DeviceCommunication.Routing;

namespace Core.Services.DeviceCommunication.Protocol;

public sealed class ProtocolSender
{
    private static readonly byte[] FrameMagic = Encoding.ASCII.GetBytes("KDC1");
    private readonly ILocalDataListener _listener;

    public ProtocolSender(ILocalDataListener listener)
    {
        _listener = listener;
    }

    public Task SendEnvelopeAsync(
        MessageContext context,
        DataEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var frame = BuildFrameHeader(payload.Length, 0);

        using var stream = new MemoryStream(frame.Length + payload.Length);
        stream.Write(frame);
        stream.Write(payload);
        stream.Position = 0;

        return _listener.SendAsync(
            context.Protocol,
            stream,
            context.RemoteEndPoint,
            context.RemoteIdentityPublicKey,
            cancellationToken);
    }

    public Task SendEnvelopeWithPayloadAsync(
        MessageContext context,
        DataEnvelope envelope,
        Stream payloadStream,
        CancellationToken cancellationToken = default)
    {
        if (!payloadStream.CanRead)
        {
            throw new InvalidOperationException("Payload stream must be readable.");
        }

        if (!payloadStream.CanSeek)
        {
            throw new InvalidOperationException("Payload stream must be seekable.");
        }

        var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var payloadLength = payloadStream.Length - payloadStream.Position;
        if (payloadLength < 0)
        {
            throw new InvalidOperationException("Invalid payload stream length.");
        }

        var frameHeader = BuildFrameHeader(envelopeBytes.Length, payloadLength);
        var pipe = new Pipe();

        async Task ProduceAsync()
        {
            Exception? error = null;
            try
            {
                await pipe.Writer.WriteAsync(frameHeader, cancellationToken);
                await pipe.Writer.WriteAsync(envelopeBytes, cancellationToken);

                var buffer = new byte[64 * 1024];
                while (true)
                {
                    var read = await payloadStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    await pipe.Writer.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                await pipe.Writer.CompleteAsync(error);
            }
        }

        var producer = ProduceAsync();

        async Task SendAsync()
        {
            Exception? sendError = null;
            try
            {
                await _listener.SendAsync(
                    context.Protocol,
                    pipe.Reader,
                    context.RemoteEndPoint,
                    context.RemoteIdentityPublicKey,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                sendError = ex;
                throw;
            }
            finally
            {
                await pipe.Reader.CompleteAsync(sendError);
            }
        }

        return SendAndAwaitProducerAsync(SendAsync(), producer);
    }

    private static async Task SendAndAwaitProducerAsync(Task sendTask, Task producerTask)
    {
        await sendTask;
        await producerTask;
    }

    private static byte[] BuildFrameHeader(int envelopeLength, long payloadLength)
    {
        var header = new byte[16];
        FrameMagic.CopyTo(header, 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), envelopeLength);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(8, 8), payloadLength);
        return header;
    }
}
