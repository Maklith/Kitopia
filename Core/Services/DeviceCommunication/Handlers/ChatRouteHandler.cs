using System.IO.Pipelines;
using Core.Services.DeviceCommunication.Application;
using Core.Services.DeviceCommunication.Codecs;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Routing;

namespace Core.Services.DeviceCommunication.Handlers;

public sealed class ChatRouteHandler : IRouteHandler
{
    private readonly MessageCodecRegistry _codecRegistry;
    private readonly IIncomingMessageSink _incomingMessageSink;

    public ChatRouteHandler(MessageCodecRegistry codecRegistry, IIncomingMessageSink incomingMessageSink)
    {
        _codecRegistry = codecRegistry;
        _incomingMessageSink = incomingMessageSink;
    }

    public string Route => "chat";

    public ValueTask HandleAsync(
        MessageContext context,
        DataEnvelope envelope,
        PipeReader payload,
        CancellationToken cancellationToken = default)
    {
        _ = context;
        _ = payload;
        _ = cancellationToken;

        if (!_codecRegistry.TryGetByEnvelope(envelope.Route, envelope.Command, out var codec))
        {
            return ValueTask.CompletedTask;
        }

        if (!codec.TryDecode(envelope, out var message))
        {
            return ValueTask.CompletedTask;
        }

        return envelope.StreamType switch
        {
            DataStreamType.Text => _incomingMessageSink.PublishAsync(message, cancellationToken),
            DataStreamType.Image => _incomingMessageSink.PublishAsync(message, cancellationToken),
            DataStreamType.File => _incomingMessageSink.PublishAsync(message, cancellationToken),
            _ => ValueTask.FromException(new InvalidOperationException($"Unsupported chat stream type: {envelope.StreamType}"))
        };
    }
}
