using Core.Services.Config;
using Core.Services.DeviceCommunication.Messages;
using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Routing;

namespace Core.Services.DeviceCommunication.Codecs;

public sealed class ChatMessageCodec : IMessageCodec
{
    public string Route => "chat";
    public string Command => "text";
    public Type MessageType => typeof(TextChatMessage);

    public bool TryEncode(AppMessage message, out DataEnvelope envelope)
    {
        if (message is not TextChatMessage chat || string.IsNullOrWhiteSpace(chat.Text))
        {
            envelope = new DataEnvelope();
            return false;
        }

        envelope = new DataEnvelope
        {
            Route = Route,
            Command = Command,
            StreamType = DataStreamType.Text,
            ChannelId = Guid.Empty,
            Sequence = 0,
            ContentType = "text/plain",
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = chat.ConversationId,
                ["senderId"] = ResolveSenderId(),
                ["text"] = chat.Text
            }
        };
        return true;
    }

    public bool TryDecode(DataEnvelope envelope, out AppMessage message)
    {
        message = null!;
        if (!string.Equals(envelope.Route, Route, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(envelope.Command, Command, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var conversationId = envelope.Metadata?.TryGetValue("senderId", out var sid) == true && !string.IsNullOrWhiteSpace(sid)
            ? sid
            : (envelope.Metadata?.TryGetValue("conversationId", out var cid) == true ? cid : string.Empty);
        var text = envelope.Metadata?.TryGetValue("text", out var t) == true ? t : string.Empty;
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        message = new TextChatMessage(conversationId!, text!);
        return true;
    }

    private static string ResolveSenderId()
    {
        try
        {
            return DeviceDiscoverySignature.TryDerivePublicKey(ConfigManger.Config.devicePrivateKey, out var publicKey)
                ? publicKey
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
