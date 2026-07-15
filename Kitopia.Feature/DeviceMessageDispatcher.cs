using System.IO.Pipelines;
using Kitopia.Feature.DeviceCommunication.Application;
using Kitopia.Feature.DeviceCommunication.Codecs;
using Kitopia.Feature.DeviceCommunication.Messages;
using Kitopia.Feature.DeviceCommunication.Messages.Chat;
using Kitopia.Feature.DeviceCommunication.Protocol;

namespace Kitopia.Feature.DeviceCommunication;

public sealed class DeviceMessageDispatcher
{
    private readonly Dictionary<string, Func<DataEnvelope, PipeReader, CancellationToken, ValueTask>> _routeHandlers;
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
        _routeHandlers = new Dictionary<string, Func<DataEnvelope, PipeReader, CancellationToken, ValueTask>>(StringComparer.OrdinalIgnoreCase)
        {
            ["chat"] = DispatchChatAsync,
            ["clipboard"] = DispatchClipboardAsync
        };
    }

    public ValueTask DispatchAsync(
        DataEnvelope envelope,
        PipeReader payload,
        CancellationToken cancellationToken = default)
    {
        return _routeHandlers.TryGetValue(envelope.Route, out var handler)
            ? handler(envelope, payload, cancellationToken)
            : ValueTask.CompletedTask;
    }

    private ValueTask DispatchChatAsync(
        DataEnvelope envelope,
        PipeReader payload,
        CancellationToken cancellationToken)
    {
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
        DataEnvelope envelope,
        PipeReader payload,
        CancellationToken cancellationToken)
    {
        _ = payload;

        if (!_codecRegistry.TryDecode(envelope, out var message))
        {
            return ValueTask.CompletedTask;
        }

        return _incomingMessageSink.PublishAsync(message, cancellationToken);
    }

    private async ValueTask PublishPayloadMessageAsync(
        AppMessage message,
        PipeReader payload,
        CancellationToken cancellationToken)
    {
        var payloadBytes = await ReadPayloadBytesAsync(payload, cancellationToken);
        await _incomingMessageSink.PublishEventAsync(
            DeviceMessageEventFactory.FromMessage(message, payloadBytes),
            cancellationToken);
    }

    private static async Task<byte[]?> ReadPayloadBytesAsync(
        PipeReader payload,
        CancellationToken cancellationToken)
    {
        await using var memory = new MemoryStream();
        await payload.CopyToAsync(memory, cancellationToken);
        return memory.Length == 0 ? null : memory.ToArray();
    }
}
