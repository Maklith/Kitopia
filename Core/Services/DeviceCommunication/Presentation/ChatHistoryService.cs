using System.Threading.Tasks;
using Core.Services.DeviceCommunication;

namespace Core.Services.DeviceCommunication.Presentation;

public sealed class ChatHistoryService : IChatHistoryService
{
    private readonly IDeviceChatHistoryStore _store;

    public ChatHistoryService(IDeviceChatHistoryStore store)
    {
        _store = store;
    }

    public Task AppendAsync(DeviceChatMessage message)
    {
        return _store.AppendAsync(message);
    }
}
