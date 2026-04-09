using System.Threading.Tasks;

namespace Core.Services.DeviceCommunication.Protocol;

internal interface IDevicePacketActions
{
    Task HandleMessagePacketAsync(PacketContext context);
    Task HandleClipboardTextPacketAsync(PacketContext context);
    Task HandleClipboardSyncRequestPacketAsync(PacketContext context);
    Task HandleClipboardSyncResponsePacketAsync(PacketContext context);
    Task HandleFileRequestPacketAsync(PacketContext context);
    Task HandleFileResponsePacketAsync(PacketContext context);
    Task HandleFileTransferPacketAsync(PacketContext context);
    Task HandleLegacyPacketAsync(PacketContext context);
    Task HandleUnknownPacketAsync(PacketContext context);
}
