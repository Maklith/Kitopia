using System.IO.Pipelines;
using Core.Services.DeviceCommunication.Application;
using Core.Services.DeviceCommunication.Codecs;
using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Routing;
using Core.Services.DeviceCommunication.Sessions;

namespace Core.Services.DeviceCommunication;

public sealed class DeviceMessageDispatcher
{
    private readonly Dictionary<string, Func<MessageContext, DataEnvelope, PipeReader, CancellationToken, ValueTask>> _routeHandlers;
    private readonly MessageCodecRegistry _codecRegistry;
    private readonly IIncomingMessageSink _incomingMessageSink;
    private readonly FileTransferPayloadHandler _fileTransferPayloadHandler;

    public DeviceMessageDispatcher(
        MessageCodecRegistry codecRegistry,
        IIncomingMessageSink incomingMessageSink,
        FileTransferPayloadHandler fileTransferPayloadHandler)
    {
        _codecRegistry = codecRegistry;
        _incomingMessageSink = incomingMessageSink;
        _fileTransferPayloadHandler = fileTransferPayloadHandler;
        _routeHandlers = new Dictionary<string, Func<MessageContext, DataEnvelope, PipeReader, CancellationToken, ValueTask>>(StringComparer.OrdinalIgnoreCase)
        {
            ["chat"] = DispatchChatAsync,
            ["clipboard"] = DispatchClipboardAsync
        };
    }

    public ValueTask DispatchAsync(
        MessageContext context,
        DataEnvelope envelope,
        PipeReader payload,
        CancellationToken cancellationToken = default)
    {
        return _routeHandlers.TryGetValue(envelope.Route, out var handler)
            ? handler(context, envelope, payload, cancellationToken)
            : ValueTask.CompletedTask;
    }

    private ValueTask DispatchChatAsync(
        MessageContext context,
        DataEnvelope envelope,
        PipeReader payload,
        CancellationToken cancellationToken)
    {
        _ = context;

        if (!_codecRegistry.TryDecode(envelope, out var message))
        {
            return ValueTask.CompletedTask;
        }

        if (message is FileChatMessage fileMessage)
        {
            return _fileTransferPayloadHandler.HandleAsync(fileMessage, payload, cancellationToken);
        }

        if (message is ImageChatMessage)
        {
            return PublishPayloadMessageAsync(message, payload, cancellationToken);
        }

        return _incomingMessageSink.PublishAsync(message, cancellationToken);
    }

    private ValueTask DispatchClipboardAsync(
        MessageContext context,
        DataEnvelope envelope,
        PipeReader payload,
        CancellationToken cancellationToken)
    {
        _ = context;
        _ = payload;

        if (!_codecRegistry.TryDecode(envelope, out var message))
        {
            return ValueTask.CompletedTask;
        }

        return _incomingMessageSink.PublishAsync(message, cancellationToken);
    }

    private async ValueTask PublishPayloadMessageAsync(
        Messages.AppMessage message,
        PipeReader payload,
        CancellationToken cancellationToken)
    {
        var payloadBytes = await ReadPayloadBytesAsync(payload, cancellationToken);
        await _incomingMessageSink.PublishEventAsync(new IncomingMessageEvent(message, PayloadBytes: payloadBytes),
            cancellationToken);
    }

    private static async Task<byte[]?> ReadPayloadBytesAsync(PipeReader payload, CancellationToken cancellationToken)
    {
        await using var memory = new MemoryStream();
        await payload.CopyToAsync(memory, cancellationToken);

        return memory.Length == 0 ? null : memory.ToArray();
    }

}
