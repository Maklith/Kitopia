using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using Core.Services.DeviceCommunication.Application;
using Core.Services.DeviceCommunication.Codecs;
using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Routing;
using Core.Services.DeviceCommunication.Sessions;

namespace Core.Services.DeviceCommunication.Handlers;

public sealed class ChatRouteHandler : IRouteHandler
{
    private readonly MessageCodecRegistry _codecRegistry;
    private readonly IIncomingMessageSink _incomingMessageSink;
    private readonly IFileTransferSessionStore _fileTransferSessionStore;

    public ChatRouteHandler(
        MessageCodecRegistry codecRegistry,
        IIncomingMessageSink incomingMessageSink,
        IFileTransferSessionStore fileTransferSessionStore)
    {
        _codecRegistry = codecRegistry;
        _incomingMessageSink = incomingMessageSink;
        _fileTransferSessionStore = fileTransferSessionStore;
    }

    public string Route => "chat";

    public ValueTask HandleAsync(
        MessageContext context,
        DataEnvelope envelope,
        PipeReader payload,
        CancellationToken cancellationToken = default)
    {
        _ = context;

        if (!_codecRegistry.TryGetByEnvelope(envelope.Route, envelope.Command, out var codec))
        {
            return ValueTask.CompletedTask;
        }

        if (!codec.TryDecode(envelope, out var message))
        {
            return ValueTask.CompletedTask;
        }

        if (message is ImageChatMessage || message is FileChatMessage)
        {
            return HandlePayloadMessageAsync(message, payload, cancellationToken);
        }

        return envelope.StreamType switch
        {
            DataStreamType.Text => _incomingMessageSink.PublishAsync(message, cancellationToken),
            DataStreamType.Image => _incomingMessageSink.PublishAsync(message, cancellationToken),
            DataStreamType.File => _incomingMessageSink.PublishAsync(message, cancellationToken),
            DataStreamType.Control => _incomingMessageSink.PublishAsync(message, cancellationToken),
            _ => ValueTask.FromException(new InvalidOperationException($"Unsupported chat stream type: {envelope.StreamType}"))
        };
    }

    private async ValueTask HandlePayloadMessageAsync(
        Core.Services.DeviceCommunication.Messages.AppMessage message,
        PipeReader payload,
        CancellationToken cancellationToken)
    {
        if (message is not FileChatMessage fileMessage)
        {
            var payloadBytes = await ReadPayloadBytesAsync(payload, cancellationToken);
            await _incomingMessageSink.PublishEventAsync(new IncomingMessageEvent(message, PayloadBytes: payloadBytes),
                cancellationToken);
            return;
        }

        await HandleFilePayloadAsync(fileMessage, payload, cancellationToken);
    }

    private async ValueTask HandleFilePayloadAsync(
        FileChatMessage message,
        PipeReader payload,
        CancellationToken cancellationToken)
    {
        if (!_fileTransferSessionStore.TryGet(message.ChannelId, out var session) ||
            session.State != FileTransferState.Accepted ||
            string.IsNullOrWhiteSpace(session.SavePath))
        {
            await DrainPayloadAsync(message, payload, cancellationToken);
            return;
        }

        var totalBytes = Math.Max(0L, message.Length ?? 0L);
        var directory = Path.GetDirectoryName(session.SavePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var fileStream = new FileStream(
            session.SavePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            useAsync: true);

        long receivedBytes = 0;
        long lastReportedBytes = 0;
        const int progressStepBytes = 1024 * 1024;

        while (true)
        {
            var readResult = await payload.ReadAsync(cancellationToken);
            var buffer = readResult.Buffer;
            if (!buffer.IsEmpty)
            {
                foreach (var segment in buffer)
                {
                    if (segment.IsEmpty)
                    {
                        continue;
                    }

                    await fileStream.WriteAsync(segment, cancellationToken);
                    receivedBytes += segment.Length;
                }

                var progressTotal = totalBytes > 0 ? totalBytes : Math.Max(receivedBytes, 1L);
                if (receivedBytes - lastReportedBytes >= progressStepBytes || readResult.IsCompleted)
                {
                    var progressEvent = new IncomingMessageEvent(
                        message,
                        IncomingMessageEventType.TransferProgress,
                        message.ChannelId,
                        receivedBytes,
                        progressTotal);
                    await _incomingMessageSink.PublishEventAsync(progressEvent, cancellationToken);
                    lastReportedBytes = receivedBytes;
                }
            }

            payload.AdvanceTo(buffer.End);
            if (readResult.IsCompleted)
            {
                break;
            }
        }

        if (receivedBytes > 0 && receivedBytes != lastReportedBytes)
        {
            var finalProgressTotal = totalBytes > 0 ? totalBytes : receivedBytes;
            await _incomingMessageSink.PublishEventAsync(
                new IncomingMessageEvent(
                    message,
                    IncomingMessageEventType.TransferProgress,
                    message.ChannelId,
                    receivedBytes,
                    finalProgressTotal),
                cancellationToken);
        }

        await fileStream.FlushAsync(cancellationToken);
        _fileTransferSessionStore.TryUpdateState(message.ChannelId, FileTransferState.Accepted, FileTransferState.Completed);
        _fileTransferSessionStore.TryRemove(message.ChannelId, out _);

        await _incomingMessageSink.PublishEventAsync(
            new IncomingMessageEvent(
                new FileCompleteChatMessage(message.ConversationId, message.ChannelId),
                IncomingMessageEventType.TransferCompleted,
                message.ChannelId,
                receivedBytes,
                Math.Max(receivedBytes, totalBytes)),
            cancellationToken);
    }

    private async ValueTask DrainPayloadAsync(
        FileChatMessage message,
        PipeReader payload,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var readResult = await payload.ReadAsync(cancellationToken);
            var buffer = readResult.Buffer;
            payload.AdvanceTo(buffer.End);
            if (readResult.IsCompleted)
            {
                break;
            }
        }

        await _incomingMessageSink.PublishEventAsync(
            new IncomingMessageEvent(
                new FileRejectChatMessage(message.ConversationId, message.ChannelId, "missing_accept_session"),
                IncomingMessageEventType.TransferRejected,
                message.ChannelId,
                Reason: "missing_accept_session"),
            cancellationToken);
    }

    private static async Task<byte[]?> ReadPayloadBytesAsync(PipeReader payload, CancellationToken cancellationToken)
    {
        await using var memory = new MemoryStream();

        while (true)
        {
            var readResult = await payload.ReadAsync(cancellationToken);
            var buffer = readResult.Buffer;
            if (!buffer.IsEmpty)
            {
                foreach (var segment in buffer)
                {
                    if (!segment.IsEmpty)
                    {
                        await memory.WriteAsync(segment, cancellationToken);
                    }
                }
            }

            payload.AdvanceTo(buffer.End);
            if (readResult.IsCompleted)
            {
                break;
            }
        }

        return memory.Length == 0 ? null : memory.ToArray();
    }
}
