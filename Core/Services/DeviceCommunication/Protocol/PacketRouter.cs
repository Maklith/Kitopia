using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PluginCore;

namespace Core.Services.DeviceCommunication.Protocol;

public sealed class PacketRouter : IPacketRouter
{
    private readonly IReadOnlyDictionary<string, IPacketHandler> _handlers;
    private readonly Func<PacketContext, Task> _unknownHandler;

    public PacketRouter(IEnumerable<IPacketHandler> handlers, Func<PacketContext, Task> unknownHandler)
    {
        _handlers = handlers.ToDictionary(
            handler => handler.PacketType,
            handler => handler,
            StringComparer.OrdinalIgnoreCase);
        _unknownHandler = unknownHandler;
    }

    public async Task DispatchAsync(PacketMetadata packet, Stream dataStream, DeviceModel sender)
    {
        var context = new PacketContext(packet, dataStream, sender);
        if (_handlers.TryGetValue(packet.Type, out var handler))
        {
            await handler.HandleAsync(context);
            return;
        }

        await _unknownHandler(context);
    }
}
