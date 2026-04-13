using System.IO;
using System.Collections.ObjectModel;
using System.Threading.Channels;
using Core.Services.DeviceCommunication.Codecs;
using Core.Services.DeviceCommunication.Handlers;
using Core.Services.DeviceCommunication.Messages;
using Core.Services.DeviceCommunication.Messages.Clipboard;
using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Discovery;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Routing;
using Core.Services.DeviceCommunication.Sessions;
using PluginCore;

namespace Core.Services.DeviceCommunication.Application;

public sealed class MessageAppService : IMessageAppService {
    private readonly MessageCodecRegistry _codecRegistry;
    private readonly ProtocolSender _protocolSender;
    private readonly IncomingMessageBuffer _incomingMessageBuffer;
    private readonly ImageTransferPolicy _imageTransferPolicy;
    private readonly IFileTransferSessionStore _fileTransferSessionStore;
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;
    private readonly IToastService _toastService;
    private readonly Channel<IncomingMessageEvent> _receiveChannel = Channel.CreateUnbounded<IncomingMessageEvent>();
    private readonly object _stateSync = new();
    private bool _isMainWindowActive;
    private bool _isDeviceChatPageOpen;
    private string? _selectedConversationId;

    public MessageAppService(
        MessageCodecRegistry codecRegistry,
        ProtocolSender protocolSender,
        IncomingMessageBuffer incomingMessageBuffer,
        ImageTransferPolicy imageTransferPolicy,
        IFileTransferSessionStore fileTransferSessionStore,
        IDeviceDiscoveryService deviceDiscoveryService,
        IToastService toastService) {
        _codecRegistry = codecRegistry;
        _protocolSender = protocolSender;
        _incomingMessageBuffer = incomingMessageBuffer;
        _imageTransferPolicy = imageTransferPolicy;
        _fileTransferSessionStore = fileTransferSessionStore;
        _deviceDiscoveryService = deviceDiscoveryService;
        _toastService = toastService;

        _ = Task.Run(ProcessIncomingMessagesAsync);
    }

    public ValueTask SendTextChatAsync(MessageContext context, TextChatMessage message,
        CancellationToken cancellationToken = default) {
        return SendCoreAsync(context, message, cancellationToken);
    }

    public ValueTask SendFileChatAsync(MessageContext context, FileChatMessage message, Stream stream,
        CancellationToken cancellationToken = default) {
        return SendFileOfferFlowAsync(
            context,
            message.ConversationId,
            message.ChannelId,
            message.FileName,
            message.Length ?? 0,
            "application/octet-stream",
            stream,
            cancellationToken);
    }

    public ValueTask SendImageChatAsync(MessageContext context, ImageChatMessage message, Stream stream,
        CancellationToken cancellationToken = default) {
        if (_imageTransferPolicy.ShouldDirectSend(message.SizeBytes)) {
            var directImage = message with { IsDirect = true };
            return SendDirectImageAsync(context, directImage, stream, cancellationToken);
        }

        return SendFileOfferFlowAsync(
            context,
            message.ConversationId,
            message.TransferId,
            $"image-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}",
            message.SizeBytes,
            message.ContentType,
            stream,
            cancellationToken);
    }

    public ValueTask AcceptFileAsync(
        MessageContext context,
        Guid transferId,
        string savePath,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(savePath) || !Path.IsPathRooted(savePath)) {
            throw new InvalidOperationException("invalid_save_path");
        }

        var fileName = Path.GetFileName(savePath);
        var session = new FileTransferSession {
            ConversationId = context.RemoteIdentityPublicKey,
            TransferId = transferId,
            FileName = string.IsNullOrWhiteSpace(fileName) ? transferId.ToString("D") : fileName,
            SizeBytes = 0,
            ContentType = "application/octet-stream",
            State = FileTransferState.Accepted,
            SavePath = savePath
        };

        if (!_fileTransferSessionStore.TryAdd(session)) {
            _fileTransferSessionStore.TryRemove(transferId, out _);
            _fileTransferSessionStore.TryAdd(session);
        }

        var message = new FileAcceptChatMessage(context.RemoteIdentityPublicKey, transferId);
        return SendCoreAsync(context, message, cancellationToken);
    }

    public ValueTask RejectFileAsync(
        MessageContext context,
        Guid transferId,
        string reason,
        CancellationToken cancellationToken = default) {
        var message = new FileRejectChatMessage(context.RemoteIdentityPublicKey, transferId, reason);
        return SendCoreAsync(context, message, cancellationToken);
    }

    public ValueTask CancelTransferAsync(
        MessageContext context,
        Guid transferId,
        string reason,
        CancellationToken cancellationToken = default) {
        var message = new FileCancelChatMessage(context.RemoteIdentityPublicKey, transferId, reason);
        return SendCoreAsync(context, message, cancellationToken);
    }

    public ValueTask SendClipboardTextAsync(MessageContext context, TextClipboardMessage message,
        CancellationToken cancellationToken = default) {
        return SendCoreAsync(context, message, cancellationToken);
    }

    public IAsyncEnumerable<IncomingMessageEvent> ReceiveAsync(CancellationToken cancellationToken = default) {
        return _receiveChannel.Reader.ReadAllAsync(cancellationToken);
    }

    public void UpdateDisplayContext(bool isMainWindowActive, bool isDeviceChatPageOpen,
        string? selectedConversationId) {
        lock (_stateSync) {
            _isMainWindowActive = isMainWindowActive;
            _isDeviceChatPageOpen = isDeviceChatPageOpen;
            _selectedConversationId = string.IsNullOrWhiteSpace(selectedConversationId)
                ? null
                : selectedConversationId;
        }
    }

    public IncomingMessageDisplayMode ResolveIncomingDisplayMode(string conversationId) {
        bool isMainWindowActive;
        bool isDeviceChatPageOpen;
        string? selectedConversationId;
        lock (_stateSync) {
            isMainWindowActive = _isMainWindowActive;
            isDeviceChatPageOpen = _isDeviceChatPageOpen;
            selectedConversationId = _selectedConversationId;
        }

        return ResolveIncomingDisplayMode(
            isMainWindowActive,
            isDeviceChatPageOpen,
            conversationId,
            selectedConversationId);
    }

    public IncomingMessageDisplayMode ResolveIncomingDisplayMode(
        bool isMainWindowActive,
        bool isDeviceChatPageOpen,
        string conversationId,
        string? selectedConversationId) {
        if (isMainWindowActive &&
            isDeviceChatPageOpen &&
            !string.IsNullOrWhiteSpace(selectedConversationId) &&
            string.Equals(conversationId, selectedConversationId, StringComparison.Ordinal)) {
            return IncomingMessageDisplayMode.ShowInCurrentConversation;
        }

        return IncomingMessageDisplayMode.NotifyByToast;
    }

    private async Task ProcessIncomingMessagesAsync() {
        await foreach (var messageEvent in _incomingMessageBuffer.ReceiveAsync()) {
            var transformedEvent = await TransformIncomingEventAsync(messageEvent);
            if (transformedEvent is null) {
                continue;
            }

            await NotifyToastIfNeededAsync(transformedEvent);
            await _receiveChannel.Writer.WriteAsync(transformedEvent);
        }
    }

    private Task<IncomingMessageEvent?> TransformIncomingEventAsync(IncomingMessageEvent messageEvent) {
        if (messageEvent.Message is not FileChatMessage) {
            return Task.FromResult<IncomingMessageEvent?>(messageEvent);
        }

        if (messageEvent.EventType == IncomingMessageEventType.TransferProgress) {
            return Task.FromResult<IncomingMessageEvent?>(messageEvent);
        }

        return Task.FromResult<IncomingMessageEvent?>(messageEvent);
    }

    private Task NotifyToastIfNeededAsync(IncomingMessageEvent messageEvent) {
        var conversationId = TryGetConversationId(messageEvent.Message);
        if (string.IsNullOrWhiteSpace(conversationId) ||
            ResolveIncomingDisplayMode(conversationId) != IncomingMessageDisplayMode.NotifyByToast) {
            return Task.CompletedTask;
        }

        var displayName = ResolveConversationDisplayName(conversationId);
        switch (messageEvent.Message) {
            case TextChatMessage textMessage: {
                var text = textMessage.Text.Trim();
                if (!string.IsNullOrWhiteSpace(text)) {
                    _toastService.Show("Device Chat", $"{displayName}: {text}");
                }

                break;
            }
            case ImageChatMessage:
                _toastService.Show("Device Chat", $"{displayName}: [Image]");
                break;
            case FileOfferChatMessage fileOffer:
                _toastService.Show("Device Chat", $"{displayName} sends file: {fileOffer.FileName}");
                break;
        }

        return Task.CompletedTask;
    }

    private string ResolveConversationDisplayName(string conversationId) {
        return conversationId;
    }

    private static string? TryGetConversationId(AppMessage message) {
        return message switch {
            TextChatMessage textMessage => textMessage.ConversationId,
            ImageChatMessage imageChatMessage => imageChatMessage.ConversationId,
            FileOfferChatMessage fileOfferChatMessage => fileOfferChatMessage.ConversationId,
            FileCompleteChatMessage fileCompleteChatMessage => fileCompleteChatMessage.ConversationId,
            FileRejectChatMessage fileRejectChatMessage => fileRejectChatMessage.ConversationId,
            FileCancelChatMessage fileCancelChatMessage => fileCancelChatMessage.ConversationId,
            _ => null
        };
    }

    private async ValueTask SendCoreAsync(MessageContext context, AppMessage message,
        CancellationToken cancellationToken) {
        if (!_codecRegistry.TryGetByMessage(message, out var codec)) {
            throw new InvalidOperationException($"No codec for message type {message.GetType().Name}.");
        }

        if (!codec.TryEncode(message, out var envelope)) {
            throw new InvalidOperationException($"Encode failed for message type {message.GetType().Name}.");
        }

        await _protocolSender.SendEnvelopeAsync(context, envelope, cancellationToken);
    }

    private async ValueTask SendFileOfferFlowAsync(
        MessageContext context,
        string conversationId,
        Guid transferId,
        string fileName,
        long sizeBytes,
        string? contentType,
        Stream payloadStream,
        CancellationToken cancellationToken) {
        var offer = new FileOfferChatMessage(conversationId, transferId, fileName, sizeBytes, contentType);
        var session = new FileTransferSession {
            ConversationId = conversationId,
            TransferId = transferId,
            FileName = fileName,
            SizeBytes = sizeBytes,
            ContentType = contentType,
            State = FileTransferState.Offered
        };

        if (!_fileTransferSessionStore.TryAdd(session)) {
            throw new InvalidOperationException("Transfer already exists.");
        }

        try {
            await SendCoreAsync(context, offer, cancellationToken);

            var accepted = await _incomingMessageBuffer.WaitForDecisionAsync(
                transferId,
                TimeSpan.FromSeconds(30),
                cancellationToken);

            if (!accepted) {
                _fileTransferSessionStore.TryUpdateState(transferId, FileTransferState.Offered,
                    FileTransferState.Rejected);
                await _incomingMessageBuffer.PublishEventAsync(
                    new IncomingMessageEvent(
                        new FileRejectChatMessage(conversationId, transferId, "timeout_or_rejected"),
                        IncomingMessageEventType.TransferTimeout,
                        transferId,
                        Reason: "timeout_or_rejected"),
                    cancellationToken);
                throw new InvalidOperationException("Transfer rejected or timed out.");
            }

            _fileTransferSessionStore.TryUpdateState(transferId, FileTransferState.Offered, FileTransferState.Accepted);

            var fileMessage = new FileChatMessage(conversationId, transferId, fileName, sizeBytes);
            if (!_codecRegistry.TryGetByMessage(fileMessage, out var fileCodec))
            {
                throw new InvalidOperationException($"No codec for message type {fileMessage.GetType().Name}.");
            }

            if (!fileCodec.TryEncode(fileMessage, out var fileEnvelope))
            {
                throw new InvalidOperationException($"Encode failed for message type {fileMessage.GetType().Name}.");
            }

            var transferEnvelope = new DataEnvelope
            {
                Route = fileEnvelope.Route,
                Command = fileEnvelope.Command,
                StreamType = fileEnvelope.StreamType,
                ChannelId = fileEnvelope.ChannelId,
                Sequence = fileEnvelope.Sequence,
                ContentType = string.IsNullOrWhiteSpace(contentType) ? fileEnvelope.ContentType : contentType,
                Metadata = fileEnvelope.Metadata
            };

            await _protocolSender.SendEnvelopeWithPayloadAsync(
                context,
                transferEnvelope,
                payloadStream,
                (sentBytes, totalBytes) => _incomingMessageBuffer.PublishEventAsync(
                    new IncomingMessageEvent(
                        fileMessage,
                        IncomingMessageEventType.TransferProgress,
                        transferId,
                        sentBytes,
                        totalBytes),
                    cancellationToken),
                cancellationToken);

            _fileTransferSessionStore.TryUpdateState(transferId, FileTransferState.Accepted,
                FileTransferState.Completed);
            _fileTransferSessionStore.TryRemove(transferId, out _);

            await _incomingMessageBuffer.PublishEventAsync(
                new IncomingMessageEvent(
                    new FileCompleteChatMessage(conversationId, transferId),
                    IncomingMessageEventType.TransferCompleted,
                    transferId,
                    sizeBytes,
                    sizeBytes),
                cancellationToken);
        }
        catch {
            _fileTransferSessionStore.TryRemove(transferId, out _);
            throw;
        }
    }

    private async ValueTask SendDirectImageAsync(
        MessageContext context,
        ImageChatMessage message,
        Stream payloadStream,
        CancellationToken cancellationToken) {
        if (!_codecRegistry.TryGetByMessage(message, out var codec)) {
            throw new InvalidOperationException($"No codec for message type {message.GetType().Name}.");
        }

        if (!codec.TryEncode(message, out var envelope)) {
            throw new InvalidOperationException($"Encode failed for message type {message.GetType().Name}.");
        }

        await _protocolSender.SendEnvelopeWithPayloadAsync(context, envelope, payloadStream, cancellationToken: cancellationToken);
    }
}
