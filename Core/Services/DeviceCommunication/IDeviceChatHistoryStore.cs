namespace Core.Services.DeviceCommunication;

public interface IDeviceChatHistoryStore
{
    event EventHandler<DeviceChatMessage>? MessageStored;

    Task AppendAsync(DeviceChatMessage message, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceChatConversation>> GetConversationsAsync(int limit = 200,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceChatMessage>> GetMessagesAsync(string peerKey, int limit = 300,
        long? beforeMessageId = null, CancellationToken cancellationToken = default);
}
