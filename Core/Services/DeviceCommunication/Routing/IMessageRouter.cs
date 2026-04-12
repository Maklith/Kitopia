using System.IO.Pipelines;
using Core.Services.DeviceCommunication.Protocol;

namespace Core.Services.DeviceCommunication.Routing;

public interface IMessageRouter
{
    ValueTask RouteAsync(
        MessageContext context,
        DataEnvelope envelope,
        PipeReader payload,
        CancellationToken cancellationToken = default);
}
