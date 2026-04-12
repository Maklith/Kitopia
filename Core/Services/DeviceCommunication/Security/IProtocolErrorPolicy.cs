using Core.Services.DeviceCommunication.Routing;

namespace Core.Services.DeviceCommunication.Security;

public interface IProtocolErrorPolicy
{
    ProtocolErrorScope ResolveScope(ProtocolError error);

    ValueTask HandleAsync(
        MessageContext context,
        ProtocolError error,
        CancellationToken cancellationToken = default);
}
