using System;

namespace Core.Services.DeviceCommunication;

public sealed class LocalDataChatCommandParser : ILocalDataBusMessageCodec<LocalDataChatMessage>
{
    private const string MessageRoute = "message";
    private const string MessageCommandPublish = "publish";

    public Type MessageType => typeof(LocalDataChatMessage);
    public string Route => MessageRoute;

    public bool CanHandleCommand(string? command)
    {
        return string.IsNullOrWhiteSpace(command) ||
               string.Equals(command, MessageCommandPublish, StringComparison.OrdinalIgnoreCase);
    }

    public bool TryDecode(LocalDataBusEnvelopeReceivedEventArgs envelopeArgs, out LocalDataChatMessage message)
    {
        message = null!;
        if (!string.Equals(envelopeArgs.Envelope.Route, MessageRoute, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!CanHandleCommand(envelopeArgs.Envelope.Command))
        {
            return false;
        }

        var text = envelopeArgs.Envelope.Message;
        if (string.IsNullOrWhiteSpace(text) &&
            envelopeArgs.Envelope.Metadata is not null &&
            envelopeArgs.Envelope.Metadata.TryGetValue("message", out var messageFromMetadata))
        {
            text = messageFromMetadata;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        message = new LocalDataChatMessage(text.Trim());
        return true;
    }

    public bool TryEncode(LocalDataChatMessage message, out LocalDataBusEnvelope envelope)
    {
        envelope = new LocalDataBusEnvelope();
        if (message is null || string.IsNullOrWhiteSpace(message.Text))
        {
            return false;
        }

        envelope = new LocalDataBusEnvelope
        {
            Route = MessageRoute,
            Command = MessageCommandPublish,
            ContentType = "text/plain",
            Message = message.Text.Trim()
        };
        return true;
    }

    bool ILocalDataBusMessageCodec.TryDecode(LocalDataBusEnvelopeReceivedEventArgs envelopeArgs, out object message)
    {
        var succeeded = TryDecode(envelopeArgs, out LocalDataChatMessage parsed);
        message = succeeded ? parsed : null!;
        return succeeded;
    }

    bool ILocalDataBusMessageCodec.TryEncode(object message, out LocalDataBusEnvelope envelope)
    {
        if (message is LocalDataChatMessage typedMessage)
        {
            return TryEncode(typedMessage, out envelope);
        }

        envelope = new LocalDataBusEnvelope();
        return false;
    }
}
