using System.IO.Pipelines;
using Kitopia.Feature.DeviceCommunication.Application;
using Kitopia.Feature.DeviceCommunication.Codecs;
using Kitopia.Feature.DeviceCommunication.Messages;
using Kitopia.Feature.DeviceCommunication.Messages.Chat;
using Kitopia.Feature.DeviceCommunication.Protocol;
using Kitopia.Feature.DeviceCommunication.Transport;

namespace Kitopia.Feature.DeviceCommunication;

public sealed class DeviceMessageDispatcher
{
    public const int MaximumDirectImageBytes = 5 * 1024 * 1024;

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
            ["chat"] = DispatchChatAsync
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

        if (message is ImageChatMessage imageMessage)
        {
            return PublishPayloadMessageAsync(imageMessage, payload, cancellationToken);
        }

        return _incomingMessageSink.PublishAsync(message, cancellationToken);
    }

    private async ValueTask PublishPayloadMessageAsync(
        ImageChatMessage message,
        PipeReader payload,
        CancellationToken cancellationToken)
    {
        if (message.SizeBytes is <= 0 or > MaximumDirectImageBytes)
        {
            throw new InvalidDataException("Direct image size exceeds the allowed range.");
        }

        var payloadBytes = await LocalDataPipeIo.ReadExactlyAsync(payload, (int)message.SizeBytes,
            cancellationToken);
        var trailing = await payload.ReadAsync(cancellationToken);
        var hasTrailingBytes = !trailing.Buffer.IsEmpty;
        payload.AdvanceTo(trailing.Buffer.Start, trailing.Buffer.End);
        if (hasTrailingBytes)
        {
            throw new InvalidDataException("Direct image payload exceeds its declared size.");
        }

        await _incomingMessageSink.PublishEventAsync(
            DeviceMessageEventFactory.FromMessage(message, payloadBytes),
            cancellationToken);
    }
}
