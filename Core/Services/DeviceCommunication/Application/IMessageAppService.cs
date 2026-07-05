using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Messages.Clipboard;

namespace Core.Services.DeviceCommunication.Application;

public enum IncomingMessageDisplayMode
{
    ShowInCurrentConversation = 1,
    NotifyByToast = 2
}

public interface IMessageAppService
{
    ValueTask SendTextChatAsync(string deviceId, string text, CancellationToken cancellationToken = default);
    ValueTask SendFileChatAsync(string deviceId, FileChatMessage message, Stream stream, CancellationToken cancellationToken = default);
    ValueTask SendImageChatAsync(string deviceId, ImageChatMessage message, Stream stream, CancellationToken cancellationToken = default);
    ValueTask AcceptFileAsync(string deviceId, Guid transferId, string savePath, CancellationToken cancellationToken = default);
    ValueTask AcceptFileAsync(
        string deviceId,
        Guid transferId,
        string saveTarget,
        Func<CancellationToken, ValueTask<Stream>> openWriteStreamAsync,
        CancellationToken cancellationToken = default);
    ValueTask RejectFileAsync(string deviceId, Guid transferId, string reason, CancellationToken cancellationToken = default);
    ValueTask CancelTransferAsync(string deviceId, Guid transferId, string reason, CancellationToken cancellationToken = default);
    ValueTask SendClipboardTextAsync(string deviceId, TextClipboardMessage message, CancellationToken cancellationToken = default);
    IAsyncEnumerable<DeviceMessageEvent> ReceiveAsync(CancellationToken cancellationToken = default);
    void UpdateDisplayContext(bool isMainWindowActive, bool isDeviceChatPageOpen, string? selectedConversationId);
    void RequestOpenConversation(string conversationId);
    string? GetRequestedConversationId();
    void ClearRequestedConversationId();
    IncomingMessageDisplayMode ResolveIncomingDisplayMode(string conversationId);
    IncomingMessageDisplayMode ResolveIncomingDisplayMode(
        bool isMainWindowActive,
        bool isDeviceChatPageOpen,
        string conversationId,
        string? selectedConversationId);
}
