namespace Core.Services.DeviceCommunication.Routing;

public sealed class RouteHandlerRegistry
{
    private readonly Dictionary<string, IRouteHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);

    public RouteHandlerRegistry(IEnumerable<IRouteHandler> handlers)
    {
        foreach (var handler in handlers)
        {
            if (!_handlers.TryAdd(handler.Route, handler))
            {
                throw new InvalidOperationException($"Duplicate route handler: {handler.Route}.");
            }
        }
    }

    public bool TryGet(string route, out IRouteHandler handler)
    {
        return _handlers.TryGetValue(route, out handler!);
    }
}
