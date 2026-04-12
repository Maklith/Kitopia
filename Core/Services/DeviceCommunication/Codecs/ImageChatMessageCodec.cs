using Core.Services.Config;
using Core.Services.DeviceCommunication.Messages;
using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Routing;

namespace Core.Services.DeviceCommunication.Codecs;

public sealed class ImageChatMessageCodec : IMessageCodec
{
    public string Route => "chat";
    public string Command => "image.direct";
    public Type MessageType => typeof(ImageChatMessage);

    public bool TryEncode(AppMessage message, out DataEnvelope envelope)
    {
        if (message is not ImageChatMessage image || !image.IsDirect)
        {
            envelope = new DataEnvelope();
            return false;
        }

        envelope = new DataEnvelope
        {
            Route = Route,
            Command = Command,
            StreamType = DataStreamType.Image,
            ChannelId = image.TransferId,
            Sequence = 0,
            ContentType = image.ContentType,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = image.ConversationId,
                ["senderId"] = ResolveSenderId(),
                ["sizeBytes"] = image.SizeBytes.ToString()
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
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return false;
        }

        long.TryParse(envelope.Metadata?.TryGetValue("sizeBytes", out var sizeText) == true ? sizeText : "0", out var sizeBytes);
        message = new ImageChatMessage(conversationId!, envelope.ChannelId, sizeBytes, envelope.ContentType, true);
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
