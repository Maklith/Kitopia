using System.IO;
using Core.Services.DeviceCommunication.Codecs;
using Core.Services.DeviceCommunication.Handlers;
using Core.Services.DeviceCommunication.Messages;
using Core.Services.DeviceCommunication.Messages.Clipboard;
using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Routing;
using Core.Services.DeviceCommunication.Sessions;

namespace Core.Services.DeviceCommunication.Application;

public sealed class MessageAppService : IMessageAppService
{
    private readonly MessageCodecRegistry _codecRegistry;
    private readonly ProtocolSender _protocolSender;
    private readonly IncomingMessageBuffer _incomingMessageBuffer;
    private readonly ImageTransferPolicy _imageTransferPolicy;
    private readonly IFileTransferSessionStore _fileTransferSessionStore;

    public MessageAppService(
        MessageCodecRegistry codecRegistry,
        ProtocolSender protocolSender,
        IncomingMessageBuffer incomingMessageBuffer,
        ImageTransferPolicy imageTransferPolicy,
        IFileTransferSessionStore fileTransferSessionStore)
    {
        _codecRegistry = codecRegistry;
        _protocolSender = protocolSender;
        _incomingMessageBuffer = incomingMessageBuffer;
        _imageTransferPolicy = imageTransferPolicy;
        _fileTransferSessionStore = fileTransferSessionStore;
    }

    public ValueTask SendTextChatAsync(MessageContext context, TextChatMessage message,
        CancellationToken cancellationToken = default)
    {
        return SendCoreAsync(context, message, cancellationToken);
    }

    public ValueTask SendFileChatAsync(MessageContext context, FileChatMessage message, Stream stream,
        CancellationToken cancellationToken = default)
    {
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
        CancellationToken cancellationToken = default)
    {
        if (_imageTransferPolicy.ShouldDirectSend(message.SizeBytes))
        {
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
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(savePath) || !Path.IsPathRooted(savePath))
        {
            throw new InvalidOperationException("invalid_save_path");
        }

        var message = new FileAcceptChatMessage(context.RemoteIdentityPublicKey, transferId);
        return SendCoreAsync(context, message, cancellationToken);
    }

    public ValueTask RejectFileAsync(
        MessageContext context,
        Guid transferId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var message = new FileRejectChatMessage(context.RemoteIdentityPublicKey, transferId, reason);
        return SendCoreAsync(context, message, cancellationToken);
    }

    public ValueTask CancelTransferAsync(
        MessageContext context,
        Guid transferId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var message = new FileCancelChatMessage(context.RemoteIdentityPublicKey, transferId, reason);
        return SendCoreAsync(context, message, cancellationToken);
    }

    public ValueTask SendClipboardTextAsync(MessageContext context, TextClipboardMessage message,
        CancellationToken cancellationToken = default)
    {
        return SendCoreAsync(context, message, cancellationToken);
    }

    public IAsyncEnumerable<IncomingMessageEvent> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        return _incomingMessageBuffer.ReceiveAsync(cancellationToken);
    }

    private async ValueTask SendCoreAsync(MessageContext context, AppMessage message, CancellationToken cancellationToken)
    {
        if (!_codecRegistry.TryGetByMessage(message, out var codec))
        {
            throw new InvalidOperationException($"No codec for message type {message.GetType().Name}.");
        }

        if (!codec.TryEncode(message, out var envelope))
        {
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
        CancellationToken cancellationToken)
    {
        var offer = new FileOfferChatMessage(conversationId, transferId, fileName, sizeBytes, contentType);
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
            await SendCoreAsync(context, offer, cancellationToken);

            var accepted = await _incomingMessageBuffer.WaitForDecisionAsync(
                transferId,
                TimeSpan.FromSeconds(30),
                cancellationToken);

            if (!accepted)
            {
                _fileTransferSessionStore.TryUpdateState(transferId, FileTransferState.Offered, FileTransferState.Rejected);
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

            await _protocolSender.SendEnvelopeWithPayloadAsync(
                context,
                new DataEnvelope
                {
                    Route = "chat",
                    Command = "file",
                    StreamType = DataStreamType.File,
                    ChannelId = transferId,
                    ContentType = contentType,
                    Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["conversationId"] = conversationId,
                        ["fileName"] = fileName,
                        ["sizeBytes"] = sizeBytes.ToString()
                    }
                },
                payloadStream,
                cancellationToken);

            _fileTransferSessionStore.TryUpdateState(transferId, FileTransferState.Accepted, FileTransferState.Completed);
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
        catch
        {
            _fileTransferSessionStore.TryRemove(transferId, out _);
            throw;
        }
    }

    private async ValueTask SendDirectImageAsync(
        MessageContext context,
        ImageChatMessage message,
        Stream payloadStream,
        CancellationToken cancellationToken)
    {
        if (!_codecRegistry.TryGetByMessage(message, out var codec))
        {
            throw new InvalidOperationException($"No codec for message type {message.GetType().Name}.");
        }

        if (!codec.TryEncode(message, out var envelope))
        {
            throw new InvalidOperationException($"Encode failed for message type {message.GetType().Name}.");
        }

        await _protocolSender.SendEnvelopeWithPayloadAsync(context, envelope, payloadStream, cancellationToken);
    }

}
