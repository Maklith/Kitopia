using System.Threading.Tasks;

namespace Core.Services.DeviceCommunication.Protocol;

internal sealed class MessagePacketHandler : IPacketHandler
{
    private readonly IDevicePacketActions _actions;
    public MessagePacketHandler(IDevicePacketActions actions) => _actions = actions;
    public string PacketType => PacketTypes.Message;
    public Task HandleAsync(PacketContext context) => _actions.HandleMessagePacketAsync(context);
}

internal sealed class ClipboardPacketHandler : IPacketHandler
{
    private readonly IDevicePacketActions _actions;
    public ClipboardPacketHandler(IDevicePacketActions actions) => _actions = actions;
    public string PacketType => PacketTypes.ClipboardText;
    public Task HandleAsync(PacketContext context) => _actions.HandleClipboardTextPacketAsync(context);
}

internal sealed class ClipboardSyncRequestHandler : IPacketHandler
{
    private readonly IDevicePacketActions _actions;
    public ClipboardSyncRequestHandler(IDevicePacketActions actions) => _actions = actions;
    public string PacketType => PacketTypes.ClipboardSyncRequest;
    public Task HandleAsync(PacketContext context) => _actions.HandleClipboardSyncRequestPacketAsync(context);
}

internal sealed class ClipboardSyncResponseHandler : IPacketHandler
{
    private readonly IDevicePacketActions _actions;
    public ClipboardSyncResponseHandler(IDevicePacketActions actions) => _actions = actions;
    public string PacketType => PacketTypes.ClipboardSyncResponse;
    public Task HandleAsync(PacketContext context) => _actions.HandleClipboardSyncResponsePacketAsync(context);
}

internal sealed class FileRequestHandler : IPacketHandler
{
    private readonly IDevicePacketActions _actions;
    public FileRequestHandler(IDevicePacketActions actions) => _actions = actions;
    public string PacketType => PacketTypes.FileRequest;
    public Task HandleAsync(PacketContext context) => _actions.HandleFileRequestPacketAsync(context);
}

internal sealed class FileResponseHandler : IPacketHandler
{
    private readonly IDevicePacketActions _actions;
    public FileResponseHandler(IDevicePacketActions actions) => _actions = actions;
    public string PacketType => PacketTypes.FileResponse;
    public Task HandleAsync(PacketContext context) => _actions.HandleFileResponsePacketAsync(context);
}

internal sealed class FileTransferHandler : IPacketHandler
{
    private readonly IDevicePacketActions _actions;
    public FileTransferHandler(IDevicePacketActions actions) => _actions = actions;
    public string PacketType => PacketTypes.FileTransfer;
    public Task HandleAsync(PacketContext context) => _actions.HandleFileTransferPacketAsync(context);
}

internal sealed class LegacyPacketHandler : IPacketHandler
{
    private readonly IDevicePacketActions _actions;
    public LegacyPacketHandler(IDevicePacketActions actions) => _actions = actions;
    public string PacketType => PacketTypes.Legacy;
    public Task HandleAsync(PacketContext context) => _actions.HandleLegacyPacketAsync(context);
}
