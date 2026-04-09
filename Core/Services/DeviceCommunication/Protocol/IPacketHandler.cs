using System.Threading.Tasks;

namespace Core.Services.DeviceCommunication.Protocol;

public interface IPacketHandler
{
    string PacketType { get; }
    Task HandleAsync(PacketContext context);
}
