using Core.Services.DeviceCommunication.Messages.Clipboard;
using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Routing;

namespace Core.Services.DeviceCommunication.Application;

public enum IncomingMessageDisplayMode
{
    ShowInCurrentConversation = 1,
    NotifyByToast = 2
}

public interface IMessageAppService
{
    ValueTask SendTextChatAsync(MessageContext context, TextChatMessage message, CancellationToken cancellationToken = default);
    ValueTask SendFileChatAsync(MessageContext context, FileChatMessage message, Stream stream, CancellationToken cancellationToken = default);
    ValueTask SendImageChatAsync(MessageContext context, ImageChatMessage message, Stream stream, CancellationToken cancellationToken = default);
    ValueTask AcceptFileAsync(MessageContext context, Guid transferId, string savePath, CancellationToken cancellationToken = default);
    ValueTask RejectFileAsync(MessageContext context, Guid transferId, string reason, CancellationToken cancellationToken = default);
    ValueTask CancelTransferAsync(MessageContext context, Guid transferId, string reason, CancellationToken cancellationToken = default);
    ValueTask SendClipboardTextAsync(MessageContext context, TextClipboardMessage message, CancellationToken cancellationToken = default);
    IAsyncEnumerable<IncomingMessageEvent> ReceiveAsync(CancellationToken cancellationToken = default);
    void UpdateDisplayContext(bool isMainWindowActive, bool isDeviceChatPageOpen, string? selectedConversationId);
    IncomingMessageDisplayMode ResolveIncomingDisplayMode(string conversationId);
    IncomingMessageDisplayMode ResolveIncomingDisplayMode(
        bool isMainWindowActive,
        bool isDeviceChatPageOpen,
        string conversationId,
        string? selectedConversationId);
}
