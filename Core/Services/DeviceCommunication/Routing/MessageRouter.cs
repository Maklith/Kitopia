using System.IO.Pipelines;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Security;

namespace Core.Services.DeviceCommunication.Routing;

public sealed class MessageRouter : IMessageRouter
{
    private readonly RouteHandlerRegistry _handlerRegistry;
    private readonly IProtocolErrorPolicy _errorPolicy;

    public MessageRouter(RouteHandlerRegistry handlerRegistry, IProtocolErrorPolicy errorPolicy)
    {
        _handlerRegistry = handlerRegistry;
        _errorPolicy = errorPolicy;
    }

    public ValueTask RouteAsync(
        MessageContext context,
        DataEnvelope envelope,
        PipeReader payload,
        CancellationToken cancellationToken = default)
    {
        if (!_handlerRegistry.TryGet(envelope.Route, out var handler))
        {
            var error = new ProtocolError(ProtocolErrorCode.RouteNotFound, $"Route not found: {envelope.Route}");
            return _errorPolicy.HandleAsync(context, error, cancellationToken);
        }

        return handler.HandleAsync(context, envelope, payload, cancellationToken);
    }
}
