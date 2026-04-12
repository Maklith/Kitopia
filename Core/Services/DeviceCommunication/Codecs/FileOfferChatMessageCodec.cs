using Core.Services.Config;
using Core.Services.DeviceCommunication.Messages;
using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Routing;

namespace Core.Services.DeviceCommunication.Codecs;

public sealed class FileOfferChatMessageCodec : IMessageCodec
{
    public string Route => "chat";
    public string Command => "file.offer";
    public Type MessageType => typeof(FileOfferChatMessage);

    public bool TryEncode(AppMessage message, out DataEnvelope envelope)
    {
        if (message is not FileOfferChatMessage offer)
        {
            envelope = new DataEnvelope();
            return false;
        }

        envelope = new DataEnvelope
        {
            Route = Route,
            Command = Command,
            StreamType = DataStreamType.File,
            ChannelId = offer.TransferId,
            Sequence = 0,
            ContentType = offer.ContentType,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = offer.ConversationId,
                ["senderId"] = ResolveSenderId(),
                ["fileName"] = offer.FileName,
                ["sizeBytes"] = offer.SizeBytes.ToString(),
                ["hash"] = offer.Hash
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
        var fileName = envelope.Metadata?.TryGetValue("fileName", out var name) == true ? name : string.Empty;
        long.TryParse(envelope.Metadata?.TryGetValue("sizeBytes", out var size) == true ? size : "0", out var sizeBytes);
        var hash = envelope.Metadata?.TryGetValue("hash", out var hashValue) == true ? hashValue : null;
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        message = new FileOfferChatMessage(conversationId!, envelope.ChannelId, fileName!, sizeBytes, envelope.ContentType, hash);
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
