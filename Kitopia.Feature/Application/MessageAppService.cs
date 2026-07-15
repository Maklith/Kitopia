using System.Threading.Channels;
using Kitopia.Feature.DeviceCommunication.Codecs;
using Kitopia.Feature.DeviceCommunication.Messages;
using Kitopia.Feature.DeviceCommunication.Messages.Chat;
using Kitopia.Feature.DeviceCommunication.Messages.Clipboard;
using Kitopia.Feature.DeviceCommunication.Protocol;
using Kitopia.Feature.DeviceCommunication.Sessions;

namespace Kitopia.Feature.DeviceCommunication.Application;

public sealed class MessageAppService : IMessageAppService
{
    private const long DirectImageThresholdBytes = 5L * 1024L * 1024L;
    private static readonly TimeSpan OfferReceiptTimeout = TimeSpan.FromSeconds(15);

    private readonly MessageCodecRegistry _codecRegistry;
    private readonly DeviceTransportService _transportService;
    private readonly IncomingMessageBuffer _incomingMessageBuffer;
    private readonly IFileTransferSessionStore _fileTransferSessionStore;
    private readonly Channel<DeviceMessageEvent> _receiveChannel = Channel.CreateUnbounded<DeviceMessageEvent>();
    private readonly object _stateSync = new();
    private bool _isMainWindowActive;
    private bool _isDeviceChatPageOpen;
    private string? _selectedConversationId;
    private string? _requestedConversationId;

    public MessageAppService(
        MessageCodecRegistry codecRegistry,
        DeviceTransportService transportService,
        IncomingMessageBuffer incomingMessageBuffer,
        IFileTransferSessionStore fileTransferSessionStore)
    {
        _codecRegistry = codecRegistry;
        _transportService = transportService;
        _incomingMessageBuffer = incomingMessageBuffer;
        _fileTransferSessionStore = fileTransferSessionStore;

        _ = Task.Run(ProcessIncomingMessagesAsync);
    }

    public ValueTask SendTextChatAsync(
        string deviceId,
        string text,
        CancellationToken cancellationToken = default)
    {
        return SendCoreAsync(deviceId, new TextChatMessage(deviceId, text), cancellationToken);
    }

    public ValueTask SendFileChatAsync(
        string deviceId,
        FileChatMessage message,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        return SendFileOfferFlowAsync(
            deviceId,
            message.ConversationId,
            message.ChannelId,
            message.FileName,
            message.Length ?? 0,
            "application/octet-stream",
            stream,
            cancellationToken,
            message.IconPng);
    }

    public ValueTask SendImageChatAsync(
        string deviceId,
        ImageChatMessage message,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        if (message.SizeBytes > 0 && message.SizeBytes <= DirectImageThresholdBytes)
        {
            return SendDirectImageAsync(deviceId, message with { IsDirect = true }, stream, cancellationToken);
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
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(savePath) || !Path.IsPathRooted(savePath))
        {
            throw new InvalidOperationException("invalid_save_path");
        }

        return AcceptFileCoreAsync(deviceId, transferId, savePath, null, cancellationToken);
    }

    public ValueTask AcceptFileAsync(
        string deviceId,
        Guid transferId,
        string displayPath,
        Func<CancellationToken, ValueTask<Stream>> openWriteStreamAsync,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(displayPath))
        {
            throw new InvalidOperationException("invalid_save_path");
        }

        ArgumentNullException.ThrowIfNull(openWriteStreamAsync);

        return AcceptFileCoreAsync(
            deviceId,
            transferId,
            displayPath,
            openWriteStreamAsync,
            cancellationToken);
    }

    private ValueTask AcceptFileCoreAsync(
        string deviceId,
        Guid transferId,
        string saveTarget,
        Func<CancellationToken, ValueTask<Stream>>? openWriteStreamAsync,
        CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(saveTarget);
        var session = new FileTransferSession
        {
            ConversationId = deviceId,
            TransferId = transferId,
            FileName = string.IsNullOrWhiteSpace(fileName) ? transferId.ToString("D") : fileName,
            SizeBytes = 0,
            ContentType = "application/octet-stream",
            State = FileTransferState.Accepted,
            SavePath = saveTarget,
            OpenWriteStreamAsync = openWriteStreamAsync
        };

        if (!_fileTransferSessionStore.TryAdd(session))
        {
            _fileTransferSessionStore.TryRemove(transferId, out _);
            _fileTransferSessionStore.TryAdd(session);
        }

        return SendCoreAsync(deviceId, new FileAcceptChatMessage(deviceId, transferId), cancellationToken);
    }

    public ValueTask RejectFileAsync(
        string deviceId,
        Guid transferId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        return SendCoreAsync(deviceId, new FileRejectChatMessage(deviceId, transferId, reason), cancellationToken);
    }

    public ValueTask CancelTransferAsync(
        string deviceId,
        Guid transferId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        return SendCoreAsync(deviceId, new FileCancelChatMessage(deviceId, transferId, reason), cancellationToken);
    }

    public ValueTask SendClipboardTextAsync(
        string deviceId,
        TextClipboardMessage message,
        CancellationToken cancellationToken = default)
    {
        return SendCoreAsync(deviceId, message, cancellationToken);
    }

    public IAsyncEnumerable<DeviceMessageEvent> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        return _receiveChannel.Reader.ReadAllAsync(cancellationToken);
    }

    public void UpdateDisplayContext(bool isMainWindowActive, bool isDeviceChatPageOpen, string? selectedConversationId)
    {
        lock (_stateSync)
        {
            _isMainWindowActive = isMainWindowActive;
            _isDeviceChatPageOpen = isMainWindowActive && isDeviceChatPageOpen;
            _selectedConversationId = !_isDeviceChatPageOpen || string.IsNullOrWhiteSpace(selectedConversationId)
                ? null
                : selectedConversationId;
        }
    }

    public void RequestOpenConversation(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        lock (_stateSync)
        {
            _requestedConversationId = conversationId;
        }
    }

    public string? GetRequestedConversationId()
    {
        lock (_stateSync)
        {
            return _requestedConversationId;
        }
    }

    public void ClearRequestedConversationId()
    {
        lock (_stateSync)
        {
            _requestedConversationId = null;
        }
    }

    public IncomingMessageDisplayMode ResolveIncomingDisplayMode(string conversationId)
    {
        bool isMainWindowActive;
        bool isDeviceChatPageOpen;
        string? selectedConversationId;
        lock (_stateSync)
        {
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
        string? selectedConversationId)
    {
        if (isMainWindowActive &&
            isDeviceChatPageOpen &&
            !string.IsNullOrWhiteSpace(selectedConversationId) &&
            string.Equals(conversationId, selectedConversationId, StringComparison.Ordinal))
        {
            return IncomingMessageDisplayMode.ShowInCurrentConversation;
        }

        return IncomingMessageDisplayMode.NotifyByToast;
    }

    private async Task ProcessIncomingMessagesAsync()
    {
        await foreach (var messageEvent in _incomingMessageBuffer.ReceiveAsync())
        {
            await SendOfferReceiptIfNeededAsync(messageEvent);
            await _receiveChannel.Writer.WriteAsync(messageEvent);
        }
    }

    private async Task SendOfferReceiptIfNeededAsync(DeviceMessageEvent messageEvent)
    {
        if (messageEvent is not FileTransferUpdatedEvent
            {
                Direction: FileTransferDirection.Download,
                Status: FileTransferStatus.WaitingForAccept
            } offerEvent)
        {
            return;
        }

        try
        {
            await SendCoreAsync(
                offerEvent.ConversationId,
                new FileOfferReceivedChatMessage(offerEvent.ConversationId, offerEvent.TransferId),
                CancellationToken.None);
        }
        catch
        {
        }
    }

    private async ValueTask SendCoreAsync(
        string deviceId,
        AppMessage message,
        CancellationToken cancellationToken)
    {
        if (!_codecRegistry.TryEncode(message, out var envelope))
        {
            throw new InvalidOperationException($"Encode failed for message type {message.GetType().Name}.");
        }

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
        CancellationToken cancellationToken,
        byte[]? iconPng = null)
    {
        var offer = new FileOfferChatMessage(conversationId, transferId, fileName, sizeBytes, contentType, IconPng: iconPng);
        var session = new FileTransferSession
        {
            ConversationId = conversationId,
            TransferId = transferId,
            FileName = fileName,
            SizeBytes = sizeBytes,
            ContentType = contentType,
            State = FileTransferState.Offered
        };

        if (!_fileTransferSessionStore.TryAdd(session))
        {
            throw new InvalidOperationException("Transfer already exists.");
        }

        try
        {
            await SendCoreAsync(deviceId, offer, cancellationToken);

            var receipt = await _incomingMessageBuffer.WaitForOfferReceiptAsync(
                transferId,
                OfferReceiptTimeout,
                cancellationToken);

            if (receipt == TransferOfferReceipt.Received)
            {
                await _incomingMessageBuffer.PublishEventAsync(
                    new FileTransferUpdatedEvent(
                        conversationId,
                        transferId,
                        FileTransferDirection.Upload,
                        FileTransferStatus.Delivered,
                        fileName,
                        null,
                        sizeBytes,
                        null,
                        DateTimeOffset.UtcNow),
                    cancellationToken);
            }

            if (receipt != TransferOfferReceipt.Received)
            {
                _fileTransferSessionStore.TryUpdateState(
                    transferId,
                    FileTransferState.Offered,
                    FileTransferState.Rejected);

                await _incomingMessageBuffer.PublishEventAsync(
                    new FileTransferUpdatedEvent(
                        conversationId,
                        transferId,
                        FileTransferDirection.Upload,
                        FileTransferStatus.Timeout,
                        fileName,
                        null,
                        sizeBytes,
                        "offer_not_received",
                        DateTimeOffset.UtcNow),
                    cancellationToken);

                throw new InvalidOperationException("The peer did not confirm the file offer.");
            }

            var decision = await _incomingMessageBuffer.WaitForDecisionAsync(
                transferId,
                Timeout.InfiniteTimeSpan,
                cancellationToken);

            if (decision != TransferDecision.Accepted)
            {
                _fileTransferSessionStore.TryUpdateState(
                    transferId,
                    FileTransferState.Offered,
                    FileTransferState.Rejected);

                if (decision == TransferDecision.Timeout)
                {
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
                    throw new InvalidOperationException("The transfer timed out.");
                }

                throw new InvalidOperationException("The peer rejected the file.");
            }

            _fileTransferSessionStore.TryUpdateState(
                transferId,
                FileTransferState.Offered,
                FileTransferState.Accepted);

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

            _fileTransferSessionStore.TryUpdateState(
                transferId,
                FileTransferState.Accepted,
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
        catch
        {
            _fileTransferSessionStore.TryRemove(transferId, out _);
            throw;
        }
    }

    private async ValueTask SendDirectImageAsync(
        string deviceId,
        ImageChatMessage message,
        Stream payloadStream,
        CancellationToken cancellationToken)
    {
        if (!_codecRegistry.TryEncode(message, out var envelope))
        {
            throw new InvalidOperationException($"Encode failed for message type {message.GetType().Name}.");
        }

        await _transportService.SendAsync(deviceId, envelope, payloadStream, cancellationToken: cancellationToken);
    }
}
