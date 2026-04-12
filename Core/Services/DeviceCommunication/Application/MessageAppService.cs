using Core.Services.DeviceCommunication.Codecs;
using Core.Services.DeviceCommunication.Messages;
using Core.Services.DeviceCommunication.Messages.Clipboard;
using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Routing;

namespace Core.Services.DeviceCommunication.Application;

public sealed class MessageAppService : IMessageAppService
{
    private readonly MessageCodecRegistry _codecRegistry;
    private readonly ProtocolSender _protocolSender;
    private readonly IncomingMessageBuffer _incomingMessageBuffer;

    public MessageAppService(
        MessageCodecRegistry codecRegistry,
        ProtocolSender protocolSender,
        IncomingMessageBuffer incomingMessageBuffer)
    {
        _codecRegistry = codecRegistry;
        _protocolSender = protocolSender;
        _incomingMessageBuffer = incomingMessageBuffer;
    }

    public ValueTask SendTextChatAsync(MessageContext context, TextChatMessage message,
        CancellationToken cancellationToken = default)
    {
        return SendCoreAsync(context, message, cancellationToken);
    }

    public ValueTask SendFileChatAsync(MessageContext context, FileChatMessage message, Stream stream,
        CancellationToken cancellationToken = default)
    {
        _ = stream;
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

}
