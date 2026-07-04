using SharedApplication = Kitopia.DeviceCommunication.Application;
using SharedMessages = Kitopia.DeviceCommunication.Messages;
using SharedChat = Kitopia.DeviceCommunication.Messages.Chat;
using SharedClipboard = Kitopia.DeviceCommunication.Messages.Clipboard;
using CoreChat = Core.Services.DeviceCommunication.Messages.Chat;
using CoreClipboard = Core.Services.DeviceCommunication.Messages.Clipboard;

namespace Core.Services.DeviceCommunication.Application;

public sealed class DesktopSharedMessageAppService : SharedApplication.IMessageAppService
{
    private readonly Core.Services.DeviceCommunication.Application.IMessageAppService _coreService;

    public DesktopSharedMessageAppService(Core.Services.DeviceCommunication.Application.IMessageAppService coreService)
    {
        _coreService = coreService;
    }

    public ValueTask SendTextChatAsync(string deviceId, string text, CancellationToken cancellationToken = default)
    {
        return _coreService.SendTextChatAsync(deviceId, text, cancellationToken);
    }

    public ValueTask SendFileChatAsync(string deviceId, SharedChat.FileChatMessage message, Stream stream, CancellationToken cancellationToken = default)
    {
        return _coreService.SendFileChatAsync(
            deviceId,
            new CoreChat.FileChatMessage(message.ConversationId, message.ChannelId, message.FileName, message.Length),
            stream,
            cancellationToken);
    }

    public ValueTask SendImageChatAsync(string deviceId, SharedChat.ImageChatMessage message, Stream stream, CancellationToken cancellationToken = default)
    {
        return _coreService.SendImageChatAsync(
            deviceId,
            new CoreChat.ImageChatMessage(message.ConversationId, message.TransferId, message.SizeBytes, message.ContentType, message.IsDirect),
            stream,
            cancellationToken);
    }

    public ValueTask AcceptFileAsync(string deviceId, Guid transferId, string savePath, CancellationToken cancellationToken = default)
    {
        return _coreService.AcceptFileAsync(deviceId, transferId, savePath, cancellationToken);
    }

    public ValueTask RejectFileAsync(string deviceId, Guid transferId, string reason, CancellationToken cancellationToken = default)
    {
        return _coreService.RejectFileAsync(deviceId, transferId, reason, cancellationToken);
    }

    public ValueTask CancelTransferAsync(string deviceId, Guid transferId, string reason, CancellationToken cancellationToken = default)
    {
        return _coreService.CancelTransferAsync(deviceId, transferId, reason, cancellationToken);
    }

    public ValueTask SendClipboardTextAsync(string deviceId, SharedClipboard.TextClipboardMessage message, CancellationToken cancellationToken = default)
    {
        return _coreService.SendClipboardTextAsync(
            deviceId,
            new CoreClipboard.TextClipboardMessage(message.ConversationId, message.Text),
            cancellationToken);
    }

    public async IAsyncEnumerable<SharedApplication.DeviceMessageEvent> ReceiveAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var messageEvent in _coreService.ReceiveAsync(cancellationToken))
        {
            yield return ConvertEvent(messageEvent);
        }
    }

    public void UpdateDisplayContext(bool isMainWindowActive, bool isDeviceChatPageOpen, string? selectedConversationId)
    {
        _coreService.UpdateDisplayContext(isMainWindowActive, isDeviceChatPageOpen, selectedConversationId);
    }

    public void RequestOpenConversation(string conversationId)
    {
        _coreService.RequestOpenConversation(conversationId);
    }

    public string? GetRequestedConversationId()
    {
        return _coreService.GetRequestedConversationId();
    }

    public void ClearRequestedConversationId()
    {
        _coreService.ClearRequestedConversationId();
    }

    public SharedApplication.IncomingMessageDisplayMode ResolveIncomingDisplayMode(string conversationId)
    {
        return (SharedApplication.IncomingMessageDisplayMode)_coreService.ResolveIncomingDisplayMode(conversationId);
    }

    public SharedApplication.IncomingMessageDisplayMode ResolveIncomingDisplayMode(
        bool isMainWindowActive,
        bool isDeviceChatPageOpen,
        string conversationId,
        string? selectedConversationId)
    {
        return (SharedApplication.IncomingMessageDisplayMode)_coreService.ResolveIncomingDisplayMode(
            isMainWindowActive,
            isDeviceChatPageOpen,
            conversationId,
            selectedConversationId);
    }

    private static SharedApplication.DeviceMessageEvent ConvertEvent(Core.Services.DeviceCommunication.Application.DeviceMessageEvent messageEvent)
    {
        return messageEvent switch
        {
            Core.Services.DeviceCommunication.Application.ChatMessageReceivedEvent chatEvent => new SharedApplication.ChatMessageReceivedEvent(
                ConvertMessage(chatEvent.Message),
                chatEvent.PayloadBytes,
                chatEvent.ConversationId,
                chatEvent.TimestampUtc),
            Core.Services.DeviceCommunication.Application.FileTransferUpdatedEvent transferEvent => new SharedApplication.FileTransferUpdatedEvent(
                transferEvent.ConversationId,
                transferEvent.TransferId,
                (SharedApplication.FileTransferDirection)transferEvent.Direction,
                (SharedApplication.FileTransferStatus)transferEvent.Status,
                transferEvent.FileName,
                transferEvent.BytesTransferred,
                transferEvent.TotalBytes,
                transferEvent.Reason,
                transferEvent.TimestampUtc),
            _ => throw new NotSupportedException($"Unsupported device message event type: {messageEvent.GetType().FullName}")
        };
    }

    private static SharedMessages.AppMessage ConvertMessage(Core.Services.DeviceCommunication.Messages.AppMessage message)
    {
        return message switch
        {
            CoreChat.TextChatMessage text => new SharedChat.TextChatMessage(text.ConversationId, text.Text),
            CoreChat.FileChatMessage file => new SharedChat.FileChatMessage(file.ConversationId, file.ChannelId, file.FileName, file.Length),
            CoreChat.ImageChatMessage image => new SharedChat.ImageChatMessage(image.ConversationId, image.TransferId, image.SizeBytes, image.ContentType, image.IsDirect),
            CoreChat.FileOfferChatMessage fileOffer => new SharedChat.FileOfferChatMessage(fileOffer.ConversationId, fileOffer.TransferId, fileOffer.FileName, fileOffer.SizeBytes, fileOffer.ContentType, fileOffer.Hash),
            CoreChat.FileOfferReceivedChatMessage offerReceived => new SharedChat.FileOfferReceivedChatMessage(offerReceived.ConversationId, offerReceived.TransferId),
            CoreChat.FileAcceptChatMessage accept => new SharedChat.FileAcceptChatMessage(accept.ConversationId, accept.TransferId),
            CoreChat.FileRejectChatMessage reject => new SharedChat.FileRejectChatMessage(reject.ConversationId, reject.TransferId, reject.Reason),
            CoreChat.FileCancelChatMessage cancel => new SharedChat.FileCancelChatMessage(cancel.ConversationId, cancel.TransferId, cancel.Reason),
            CoreChat.FileCompleteChatMessage complete => new SharedChat.FileCompleteChatMessage(complete.ConversationId, complete.TransferId),
            CoreClipboard.TextClipboardMessage clipboard => new SharedClipboard.TextClipboardMessage(clipboard.ConversationId, clipboard.Text),
            _ => throw new NotSupportedException($"Unsupported app message type: {message.GetType().FullName}")
        };
    }
}
