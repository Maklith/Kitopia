using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Text.Json;
using Core.Services.DeviceCommunication.Routing;

namespace Core.Services.DeviceCommunication.Protocol;

public sealed class ProtocolSession
{
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
        var allBytes = new ArrayBufferWriter<byte>();
        while (true)
        {
            var readResult = await payloadReader.ReadAsync(cancellationToken);
            var buffer = readResult.Buffer;
            foreach (var segment in buffer)
            {
                allBytes.Write(segment.Span);
            }

            payloadReader.AdvanceTo(buffer.End);
            if (readResult.IsCompleted)
            {
                break;
            }
        }

        if (allBytes.WrittenCount == 0)
        {
            return;
        }

        var envelope = JsonSerializer.Deserialize<DataEnvelope>(allBytes.WrittenSpan);
        if (envelope is null || string.IsNullOrWhiteSpace(envelope.Route))
        {
            return;
        }

        var payloadPipe = new Pipe();
        await payloadPipe.Writer.CompleteAsync();
        var context = new MessageContext(protocol, remoteEndPoint, string.Empty);
        await _router.RouteAsync(context, envelope, payloadPipe.Reader, cancellationToken);
        await payloadPipe.Reader.CompleteAsync();
    }
}
