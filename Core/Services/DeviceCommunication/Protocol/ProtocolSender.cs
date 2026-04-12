using System.Text.Json;
using Core.Services.DeviceCommunication.Routing;

namespace Core.Services.DeviceCommunication.Protocol;

public sealed class ProtocolSender
{
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
        return _listener.SendAsync(
            context.Protocol,
            payload,
            context.RemoteEndPoint,
            context.RemoteIdentityPublicKey,
            cancellationToken);
    }
}
