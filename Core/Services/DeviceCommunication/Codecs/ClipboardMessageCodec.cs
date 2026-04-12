using Core.Services.DeviceCommunication.Messages;
using Core.Services.DeviceCommunication.Messages.Clipboard;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Routing;

namespace Core.Services.DeviceCommunication.Codecs;

public sealed class ClipboardMessageCodec : IMessageCodec
{
    public string Route => "clipboard";
    public string Command => "text";
    public Type MessageType => typeof(TextClipboardMessage);

    public bool TryEncode(AppMessage message, out DataEnvelope envelope)
    {
        if (message is not TextClipboardMessage clipboard || string.IsNullOrWhiteSpace(clipboard.Text))
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
                ["conversationId"] = clipboard.ConversationId,
                ["text"] = clipboard.Text
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

        var conversationId = envelope.Metadata?.TryGetValue("conversationId", out var cid) == true ? cid : string.Empty;
        var text = envelope.Metadata?.TryGetValue("text", out var t) == true ? t : string.Empty;
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        message = new TextClipboardMessage(conversationId!, text!);
        return true;
    }
}
