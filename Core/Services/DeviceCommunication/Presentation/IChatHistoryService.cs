using System.Threading.Tasks;
using Core.Services.DeviceCommunication;

namespace Core.Services.DeviceCommunication.Presentation;

public interface IChatHistoryService
{
    Task AppendAsync(DeviceChatMessage message);
}
