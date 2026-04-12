using System.IO.Pipelines;
using Core.Services.DeviceCommunication.Protocol;

namespace Core.Services.DeviceCommunication.Routing;

public interface IRouteHandler
{
    string Route { get; }

    ValueTask HandleAsync(
        MessageContext context,
        DataEnvelope envelope,
        PipeReader payload,
        CancellationToken cancellationToken = default);
}
