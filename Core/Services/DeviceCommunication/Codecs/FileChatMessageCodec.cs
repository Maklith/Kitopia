using Core.Services.Config;
using Core.Services.DeviceCommunication.Messages;
using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Routing;

namespace Core.Services.DeviceCommunication.Codecs;

public sealed class FileChatMessageCodec : IMessageCodec
{
    public string Route => "chat";
    public string Command => "file";
    public Type MessageType => typeof(FileChatMessage);

    public bool TryEncode(AppMessage message, out DataEnvelope envelope)
    {
        if (message is not FileChatMessage fileMessage || string.IsNullOrWhiteSpace(fileMessage.FileName))
        {
            envelope = new DataEnvelope();
            return false;
        }

        envelope = new DataEnvelope
        {
            Route = Route,
            Command = Command,
            StreamType = DataStreamType.File,
            ChannelId = fileMessage.ChannelId,
            Sequence = 0,
            ContentType = "application/octet-stream",
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = fileMessage.ConversationId,
                ["senderId"] = ResolveSenderId(),
                ["fileName"] = fileMessage.FileName,
                ["length"] = fileMessage.Length?.ToString()
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

        var fileName = envelope.Metadata?.TryGetValue("fileName", out var fileNameValue) == true
            ? fileNameValue
            : string.Empty;

        long? length = null;
        if (envelope.Metadata?.TryGetValue("length", out var lengthValue) == true &&
            long.TryParse(lengthValue, out var parsedLength))
        {
            length = parsedLength;
        }

        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        message = new FileChatMessage(conversationId!, envelope.ChannelId, fileName!, length);
        return true;
    }

    private static string ResolveSenderId()
    {
        return DeviceDiscoverySignature.TryDerivePublicKey(ConfigManger.Config.devicePrivateKey, out var publicKey)
            ? publicKey
            : string.Empty;
    }
}
