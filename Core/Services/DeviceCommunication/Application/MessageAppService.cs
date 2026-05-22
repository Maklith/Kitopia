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
    private static readonly TimeSpan TransferProgressLogInterval = TimeSpan.FromSeconds(2);
    private readonly MessageCodecRegistry _codecRegistry;
    private readonly DeviceTransportService _transportService;
    private readonly IncomingMessageBuffer _incomingMessageBuffer;
    private readonly ImageTransferPolicy _imageTransferPolicy;
    private readonly IFileTransferSessionStore _fileTransferSessionStore;
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;
    private readonly IToastService _toastService;
    private readonly INavigationService _navigationService;
    private readonly Channel<DeviceMessageEvent> _receiveChannel = Channel.CreateUnbounded<DeviceMessageEvent>();
    private readonly object _stateSync = new();
    private bool _isMainWindowActive;
    private bool _isDeviceChatPageOpen;
    private string? _selectedConversationId;
    private string? _requestedConversationId;
    private readonly Dictionary<Guid, DateTimeOffset> _transferProgressLogTime = [];

    public MessageAppService(
        MessageCodecRegistry codecRegistry,
        DeviceTransportService transportService,
        IncomingMessageBuffer incomingMessageBuffer,
        ImageTransferPolicy imageTransferPolicy,
        IFileTransferSessionStore fileTransferSessionStore,
        IDeviceDiscoveryService deviceDiscoveryService,
        IToastService toastService,
        INavigationService navigationService) {
        _codecRegistry = codecRegistry;
        _transportService = transportService;
        _incomingMessageBuffer = incomingMessageBuffer;
        _imageTransferPolicy = imageTransferPolicy;
        _fileTransferSessionStore = fileTransferSessionStore;
        _deviceDiscoveryService = deviceDiscoveryService;
        _toastService = toastService;
        _navigationService = navigationService;

        _ = Task.Run(ProcessIncomingMessagesAsync);
    }

    public ValueTask SendTextChatAsync(string deviceId, string text,
        CancellationToken cancellationToken = default) {
        return SendCoreAsync(deviceId, new TextChatMessage(deviceId, text), cancellationToken);
    }

    public ValueTask SendFileChatAsync(string deviceId, FileChatMessage message, Stream stream,
        CancellationToken cancellationToken = default) {
        return SendFileOfferFlowAsync(
            deviceId,
            message.ConversationId,
            message.ChannelId,
            message.FileName,
            message.Length ?? 0,
            "application/octet-stream",
            stream,
            cancellationToken);
    }

    public ValueTask SendImageChatAsync(string deviceId, ImageChatMessage message, Stream stream,
        CancellationToken cancellationToken = default) {
        if (_imageTransferPolicy.ShouldDirectSend(message.SizeBytes)) {
            var directImage = message with { IsDirect = true };
            return SendDirectImageAsync(deviceId, directImage, stream, cancellationToken);
        }

        return SendFileOfferFlowAsync(
            deviceId,
            message.ConversationId,
            message.TransferId,
            $"image-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}",
            message.SizeBytes,
            message.ContentType,
            stream,
            cancellationToken);
    }

    public ValueTask AcceptFileAsync(
        string deviceId,
        Guid transferId,
        string savePath,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(savePath) || !Path.IsPathRooted(savePath)) {
            throw new InvalidOperationException("invalid_save_path");
        }

        var fileName = Path.GetFileName(savePath);
        var session = new FileTransferSession {
            ConversationId = deviceId,
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

        var message = new FileAcceptChatMessage(deviceId, transferId);
        return SendCoreAsync(deviceId, message, cancellationToken);
    }

    public ValueTask RejectFileAsync(
        string deviceId,
        Guid transferId,
        string reason,
        CancellationToken cancellationToken = default) {
        var message = new FileRejectChatMessage(deviceId, transferId, reason);
        return SendCoreAsync(deviceId, message, cancellationToken);
    }

    public ValueTask CancelTransferAsync(
        string deviceId,
        Guid transferId,
        string reason,
        CancellationToken cancellationToken = default) {
        var message = new FileCancelChatMessage(deviceId, transferId, reason);
        return SendCoreAsync(deviceId, message, cancellationToken);
    }

    public ValueTask SendClipboardTextAsync(string deviceId, TextClipboardMessage message,
        CancellationToken cancellationToken = default) {
        return SendCoreAsync(deviceId, message, cancellationToken);
    }

    public IAsyncEnumerable<DeviceMessageEvent> ReceiveAsync(CancellationToken cancellationToken = default) {
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
            if (ShouldLogIncomingEvent(messageEvent)) {
                Logger.Information(
                    "接收信息。 EventType={EventType} ConversationId={ConversationId} TransferId={TransferId} Detail={Detail}",
                    messageEvent.GetType().Name,
                    messageEvent.ConversationId,
                    messageEvent is FileTransferUpdatedEvent transferEvent ? transferEvent.TransferId : null,
                    DescribeIncomingEvent(messageEvent));
            }

            await NotifyToastIfNeededAsync(messageEvent);
            await _receiveChannel.Writer.WriteAsync(messageEvent);
        }
    }

    private Task NotifyToastIfNeededAsync(DeviceMessageEvent messageEvent) {
        var conversationId = messageEvent.ConversationId;
        if (string.IsNullOrWhiteSpace(conversationId) ||
            ResolveIncomingDisplayMode(conversationId) != IncomingMessageDisplayMode.NotifyByToast) {
            return Task.CompletedTask;
        }

        var displayName = ResolveConversationDisplayName(conversationId);
        switch (messageEvent) {
            case ChatMessageReceivedEvent { Message: TextChatMessage textMessage }: {
                var text = textMessage.Text.Trim();
                if (!string.IsNullOrWhiteSpace(text)) {
                    ShowDeviceChatToast(conversationId, displayName, text);
                }

                break;
            }
            case ChatMessageReceivedEvent { Message: ImageChatMessage }:
                ShowDeviceChatToast(conversationId, displayName, "[图片]");
                break;
            case FileTransferUpdatedEvent { Status: FileTransferStatus.WaitingForAccept } fileOffer:
                ShowDeviceChatToast(conversationId, displayName, $"文件: {fileOffer.FileName}",TimeSpan.Zero);
                break;
            case FileTransferUpdatedEvent { Direction: FileTransferDirection.Upload } fileReject
                when fileReject.Status is FileTransferStatus.Rejected or FileTransferStatus.Timeout or FileTransferStatus.Failed:
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

    private bool ShouldLogIncomingEvent(DeviceMessageEvent messageEvent) {
        if (messageEvent is not FileTransferUpdatedEvent { Status: FileTransferStatus.InProgress } progressEvent) {
            if (messageEvent is FileTransferUpdatedEvent transferEvent) {
                var transferId = transferEvent.TransferId;
                _transferProgressLogTime.Remove(transferId);
            }

            return true;
        }

        var progressTransferId = progressEvent.TransferId;
        var isFinal = progressEvent.BytesTransferred.HasValue && progressEvent.TotalBytes.HasValue &&
                      progressEvent.TotalBytes.Value > 0 &&
                      progressEvent.BytesTransferred.Value >= progressEvent.TotalBytes.Value;
        if (isFinal) {
            _transferProgressLogTime.Remove(progressTransferId);
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        if (_transferProgressLogTime.TryGetValue(progressTransferId, out var last) &&
            now - last < TransferProgressLogInterval) {
            return false;
        }

        _transferProgressLogTime[progressTransferId] = now;
        return true;
    }

    private void ShowDeviceChatToast(string conversationId, string displayName, string text,TimeSpan? autoCloseTime = null) {
        _toastService.Show(new ToastRequest {
            Header = $"设备聊天:{displayName}",
            Text = text,
            ClickCallback = () => OpenConversationFromToast(conversationId),
            AutoCloseDelay = autoCloseTime ?? TimeSpan.FromSeconds(5)
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

    private async ValueTask SendCoreAsync(string deviceId, AppMessage message,
        CancellationToken cancellationToken) {
        if (!_codecRegistry.TryEncode(message, out var envelope)) {
            throw new InvalidOperationException($"Encode failed for message type {message.GetType().Name}.");
        }

        Logger.Information(
            "发送信息。 Type={MessageType} Protocol={Protocol} Remote={RemoteEndPoint} Route={Route} Command={Command} ConversationId={ConversationId} Detail={Detail}",
            message.GetType().Name,
            "auto",
            deviceId,
            envelope.Route,
            envelope.Command,
            message.ConversationId,
            DescribeMessage(message));

        await _transportService.SendAsync(deviceId, envelope, cancellationToken: cancellationToken);
    }

    private async ValueTask SendFileOfferFlowAsync(
        string deviceId,
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
            await SendCoreAsync(deviceId, offer, cancellationToken);

            var decision = await _incomingMessageBuffer.WaitForDecisionAsync(
                transferId,
                TimeSpan.FromSeconds(30),
                cancellationToken);

            if (decision != TransferDecision.Accepted) {
                _fileTransferSessionStore.TryUpdateState(transferId, FileTransferState.Offered,
                    FileTransferState.Rejected);

                if (decision == TransferDecision.Timeout) {
                    await _incomingMessageBuffer.PublishEventAsync(
                        new FileTransferUpdatedEvent(
                            conversationId,
                            transferId,
                            FileTransferDirection.Upload,
                            FileTransferStatus.Timeout,
                            fileName,
                            null,
                            sizeBytes,
                            "timeout",
                            DateTimeOffset.UtcNow),
                        cancellationToken);
                    throw new InvalidOperationException("文件发送超时，请稍后重试。");
                }

                throw new InvalidOperationException("对方已拒绝接收文件。");
            }

            _fileTransferSessionStore.TryUpdateState(transferId, FileTransferState.Offered, FileTransferState.Accepted);

            var fileMessage = new FileChatMessage(conversationId, transferId, fileName, sizeBytes);
            if (!_codecRegistry.TryEncode(fileMessage, out var fileEnvelope))
            {
                throw new InvalidOperationException($"Encode failed for message type {fileMessage.GetType().Name}.");
            }

            fileEnvelope = string.IsNullOrWhiteSpace(contentType)
                ? fileEnvelope
                : new DataEnvelope
                {
                    Route = fileEnvelope.Route,
                    Command = fileEnvelope.Command,
                    StreamType = fileEnvelope.StreamType,
                    ChannelId = fileEnvelope.ChannelId,
                    Sequence = fileEnvelope.Sequence,
                    ContentType = contentType,
                    Metadata = fileEnvelope.Metadata
                };

            await _transportService.SendAsync(
                deviceId,
                fileEnvelope,
                payloadStream,
                (sentBytes, totalBytes) => _incomingMessageBuffer.PublishEventAsync(
                    new FileTransferUpdatedEvent(
                        conversationId,
                        transferId,
                        FileTransferDirection.Upload,
                        FileTransferStatus.InProgress,
                        fileName,
                        sentBytes,
                        totalBytes,
                        null,
                        DateTimeOffset.UtcNow),
                    cancellationToken),
                cancellationToken);

            _fileTransferSessionStore.TryUpdateState(transferId, FileTransferState.Accepted,
                FileTransferState.Completed);
            _fileTransferSessionStore.TryRemove(transferId, out _);

            await _incomingMessageBuffer.PublishEventAsync(
                new FileTransferUpdatedEvent(
                    conversationId,
                    transferId,
                    FileTransferDirection.Upload,
                    FileTransferStatus.Completed,
                    fileName,
                    sizeBytes,
                    sizeBytes,
                    null,
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch {
            _fileTransferSessionStore.TryRemove(transferId, out _);
            throw;
        }
    }

    private async ValueTask SendDirectImageAsync(
        string deviceId,
        ImageChatMessage message,
        Stream payloadStream,
        CancellationToken cancellationToken) {
        if (!_codecRegistry.TryEncode(message, out var envelope)) {
            throw new InvalidOperationException($"Encode failed for message type {message.GetType().Name}.");
        }

        await _transportService.SendAsync(deviceId, envelope, payloadStream, cancellationToken: cancellationToken);
    }

    private static string DescribeIncomingEvent(DeviceMessageEvent messageEvent) {
        var baseDetail = messageEvent switch {
            ChatMessageReceivedEvent chat => $"{DescribeMessage(chat.Message)}, payloadBytes={chat.PayloadBytes?.LongLength ?? 0}",
            FileTransferUpdatedEvent transfer => $"transferId={transfer.TransferId}, direction={transfer.Direction}, status={transfer.Status}, file={transfer.FileName}, bytes={transfer.BytesTransferred}/{transfer.TotalBytes}",
            _ => messageEvent.ToString() ?? messageEvent.GetType().Name
        };
        var reason = messageEvent is FileTransferUpdatedEvent transferEvent ? transferEvent.Reason : null;
        return string.IsNullOrWhiteSpace(reason)
            ? baseDetail
            : $"{baseDetail}, reason={reason}";
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
