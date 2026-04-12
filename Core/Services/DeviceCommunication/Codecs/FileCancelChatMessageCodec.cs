using Core.Services.Config;
using Core.Services.DeviceCommunication.Messages;
using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Routing;

namespace Core.Services.DeviceCommunication.Codecs;

public sealed class FileCancelChatMessageCodec : IMessageCodec
{
    public string Route => "chat";
    public string Command => "file.cancel";
    public Type MessageType => typeof(FileCancelChatMessage);

    public bool TryEncode(AppMessage message, out DataEnvelope envelope)
    {
        if (message is not FileCancelChatMessage cancel)
        {
            envelope = new DataEnvelope();
            return false;
        }

        envelope = new DataEnvelope
        {
            Route = Route,
            Command = Command,
            StreamType = DataStreamType.Control,
            ChannelId = cancel.TransferId,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = cancel.ConversationId,
                ["senderId"] = ResolveSenderId(),
                ["reason"] = cancel.Reason
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
        var reason = envelope.Metadata?.TryGetValue("reason", out var reasonValue) == true ? reasonValue : string.Empty;
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return false;
        }

        message = new FileCancelChatMessage(conversationId!, envelope.ChannelId, reason ?? string.Empty);
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
