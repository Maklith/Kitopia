using System.IO;
using System.Threading.Tasks;
using PluginCore;

namespace Core.Services.DeviceCommunication.Protocol;

public interface IPacketRouter
{
    Task DispatchAsync(PacketMetadata packet, Stream dataStream, DeviceModel sender);
}
