using Core.Services.DeviceCommunication.Messages.Clipboard;
using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Routing;

namespace Core.Services.DeviceCommunication.Application;

public interface IMessageAppService
{
    ValueTask SendTextChatAsync(MessageContext context, TextChatMessage message, CancellationToken cancellationToken = default);
    ValueTask SendFileChatAsync(MessageContext context, FileChatMessage message, Stream stream, CancellationToken cancellationToken = default);
    ValueTask SendClipboardTextAsync(MessageContext context, TextClipboardMessage message, CancellationToken cancellationToken = default);
    IAsyncEnumerable<IncomingMessageEvent> ReceiveAsync(CancellationToken cancellationToken = default);
}
