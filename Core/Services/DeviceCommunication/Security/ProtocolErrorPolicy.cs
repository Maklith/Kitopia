using Core.Services.DeviceCommunication.Routing;

namespace Core.Services.DeviceCommunication.Security;

public sealed class ProtocolErrorPolicy : IProtocolErrorPolicy
{
    public ProtocolErrorScope ResolveScope(ProtocolError error)
    {
        return error.Code switch
        {
            ProtocolErrorCode.SecurityValidationFailed => ProtocolErrorScope.Connection,
            ProtocolErrorCode.InvalidFrame or ProtocolErrorCode.ChannelNotFound => ProtocolErrorScope.Session,
            _ => ProtocolErrorScope.Message
        };
    }

    public ValueTask HandleAsync(
        MessageContext context,
        ProtocolError error,
        CancellationToken cancellationToken = default)
    {
        _ = context;
        _ = error;
        return ValueTask.CompletedTask;
    }
}
