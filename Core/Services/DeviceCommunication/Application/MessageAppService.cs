using System.Threading.Channels;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Core.Services.DeviceCommunication.Codecs;
using Core.Services.DeviceCommunication.Discovery;
using Core.Services.DeviceCommunication.Handlers;
using Core.Services.DeviceCommunication.Messages;
using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Messages.Clipboard;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Routing;
using Core.Services.DeviceCommunication.Sessions;
using Core.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Serilog;

namespace Core.Services.DeviceCommunication.Application;

public sealed class MessageAppService : IMessageAppService {
    private static readonly ILogger Logger = LogManager.Logger.ForContext<MessageAppService>();
    private readonly MessageCodecRegistry _codecRegistry;
    private readonly ProtocolSender _protocolSender;
    private readonly IncomingMessageBuffer _incomingMessageBuffer;
    private readonly ImageTransferPolicy _imageTransferPolicy;
    private readonly IFileTransferSessionStore _fileTransferSessionStore;
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;
    private readonly IToastService _toastService;
    private readonly INavigationService _navigationService;
    private readonly Channel<IncomingMessageEvent> _receiveChannel = Channel.CreateUnbounded<IncomingMessageEvent>();
    private readonly object _stateSync = new();
    private bool _isMainWindowActive;
    private bool _isDeviceChatPageOpen;
    private string? _selectedConversationId;
    private string? _requestedConversationId;

    public MessageAppService(
        MessageCodecRegistry codecRegistry,
        ProtocolSender protocolSender,
        IncomingMessageBuffer incomingMessageBuffer,
        ImageTransferPolicy imageTransferPolicy,
        IFileTransferSessionStore fileTransferSessionStore,
        IDeviceDiscoveryService deviceDiscoveryService,
        IToastService toastService,
        INavigationService navigationService) {
        _codecRegistry = codecRegistry;
        _protocolSender = protocolSender;
        _incomingMessageBuffer = incomingMessageBuffer;
        _imageTransferPolicy = imageTransferPolicy;
        _fileTransferSessionStore = fileTransferSessionStore;
        _deviceDiscoveryService = deviceDiscoveryService;
        _toastService = toastService;
        _navigationService = navigationService;

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
            _isDeviceChatPageOpen = isMainWindowActive && isDeviceChatPageOpen;
            _selectedConversationId = !_isDeviceChatPageOpen || string.IsNullOrWhiteSpace(selectedConversationId)
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

    public void RequestOpenConversation(string conversationId) {
        if (string.IsNullOrWhiteSpace(conversationId)) {
            return;
        }

        lock (_stateSync) {
            _requestedConversationId = conversationId;
        }
    }

    public string? GetRequestedConversationId() {
        lock (_stateSync) {
            return _requestedConversationId;
        }
    }

    public void ClearRequestedConversationId() {
        lock (_stateSync) {
            _requestedConversationId = null;
        }
    }

    private async Task ProcessIncomingMessagesAsync() {
        await foreach (var messageEvent in _incomingMessageBuffer.ReceiveAsync()) {
            var transformedEvent = await TransformIncomingEventAsync(messageEvent);
            if (transformedEvent is null) {
                continue;
            }

            Logger.Information(
                "接收信息。 EventType={EventType} Type={MessageType} ConversationId={ConversationId} TransferId={TransferId} Detail={Detail}",
                transformedEvent.EventType,
                transformedEvent.Message.GetType().Name,
                transformedEvent.Message.ConversationId,
                transformedEvent.TransferId,
                DescribeIncomingEvent(transformedEvent));

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
                    ShowDeviceChatToast(conversationId, displayName, text);
                }

                break;
            }
            case ImageChatMessage:
                ShowDeviceChatToast(conversationId, displayName, "[图片]");
                break;
            case FileOfferChatMessage fileOffer:
                ShowDeviceChatToast(conversationId, displayName, $"文件: {fileOffer.FileName}");
                break;
            case FileRejectChatMessage fileReject:
                ShowDeviceChatToast(conversationId, displayName, ResolveRejectToastText(fileReject.Reason));
                break;
        }

        return Task.CompletedTask;
    }

    private static string ResolveRejectToastText(string? reason) {
        return reason switch {
            "rejected_by_peer" or "rejected_by_user" => "对方已拒绝接收文件",
            "timeout" => "文件发送超时，请稍后重试",
            _ => "文件发送失败"
        };
    }

    private void ShowDeviceChatToast(string conversationId, string displayName, string text) {
        _toastService.Show(new ToastRequest {
            Header = $"设备聊天:{displayName}",
            Text = text,
            ClickCallback = () => OpenConversationFromToast(conversationId)
        });
    }

    private void OpenConversationFromToast(string conversationId) {
        RequestOpenConversation(conversationId);
        _navigationService.Navigate("device/chat");
        if (Avalonia.Application.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow!.Show();
            desktop.MainWindow.WindowState = WindowState.Normal;
            ServiceManager.Services.GetService<IWindowTool>()!
                .SetForegroundWindow(desktop.MainWindow.TryGetPlatformHandle()!.Handle);
        }
    }

    private string ResolveConversationDisplayName(string conversationId) {
        if (string.IsNullOrWhiteSpace(conversationId)) {
            return conversationId;
        }

        var device = _deviceDiscoveryService.Devices.FirstOrDefault(item =>
            string.Equals(item.Id, conversationId, StringComparison.Ordinal));
        return string.IsNullOrWhiteSpace(device?.DisplayName) ? conversationId : device.DisplayName;
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

        Logger.Information(
            "发送信息。 Type={MessageType} Protocol={Protocol} Remote={RemoteEndPoint} Route={Route} Command={Command} ConversationId={ConversationId} Detail={Detail}",
            message.GetType().Name,
            context.Protocol,
            context.RemoteEndPoint,
            envelope.Route,
            envelope.Command,
            message.ConversationId,
            DescribeMessage(message));

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

            var decision = await _incomingMessageBuffer.WaitForDecisionAsync(
                transferId,
                TimeSpan.FromSeconds(30),
                cancellationToken);

            if (decision != TransferDecision.Accepted) {
                _fileTransferSessionStore.TryUpdateState(transferId, FileTransferState.Offered,
                    FileTransferState.Rejected);

                if (decision == TransferDecision.Timeout) {
                    await _incomingMessageBuffer.PublishEventAsync(
                        new IncomingMessageEvent(
                            new FileRejectChatMessage(conversationId, transferId, "timeout"),
                            IncomingMessageEventType.TransferTimeout,
                            transferId,
                            Reason: "timeout"),
                        cancellationToken);
                    throw new InvalidOperationException("文件发送超时，请稍后重试。");
                }

                throw new InvalidOperationException("对方已拒绝接收文件。");
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

    private static string DescribeIncomingEvent(IncomingMessageEvent messageEvent) {
        var payloadBytes = messageEvent.PayloadBytes?.LongLength ?? 0;
        var baseDetail = $"{DescribeMessage(messageEvent.Message)}, bytes={messageEvent.BytesTransferred}/{messageEvent.TotalBytes}, payloadBytes={payloadBytes}";
        return string.IsNullOrWhiteSpace(messageEvent.Reason)
            ? baseDetail
            : $"{baseDetail}, reason={messageEvent.Reason}";
    }

    private static string DescribeMessage(AppMessage message) {
        return message switch {
            TextChatMessage text => $"text={LimitForLog(text.Text)}",
            TextClipboardMessage textClipboard => $"clipboardText={LimitForLog(textClipboard.Text)}",
            FileOfferChatMessage fileOffer =>
                $"transferId={fileOffer.TransferId}, file={fileOffer.FileName}, size={fileOffer.SizeBytes}, contentType={fileOffer.ContentType}",
            FileChatMessage file =>
                $"channelId={file.ChannelId}, file={file.FileName}, length={file.Length}",
            ImageChatMessage image =>
                $"transferId={image.TransferId}, size={image.SizeBytes}, contentType={image.ContentType}, isDirect={image.IsDirect}",
            FileAcceptChatMessage accept => $"transferId={accept.TransferId}",
            FileRejectChatMessage reject => $"transferId={reject.TransferId}, reason={reject.Reason}",
            FileCancelChatMessage cancel => $"transferId={cancel.TransferId}, reason={cancel.Reason}",
            FileCompleteChatMessage complete => $"transferId={complete.TransferId}",
            _ => message.ToString() ?? message.GetType().Name
        };
    }

    private static string LimitForLog(string? text, int maxLength = 120) {
        if (string.IsNullOrWhiteSpace(text)) {
            return string.Empty;
        }

        var singleLine = text.ReplaceLineEndings("\\n");
        return singleLine.Length <= maxLength ? singleLine : $"{singleLine[..maxLength]}...";
    }
}
