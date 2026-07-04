using Core.Services.Config;
using Core.Services.DeviceCommunication.Messages;
using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Messages.Clipboard;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Routing;

namespace Core.Services.DeviceCommunication.Codecs;

public sealed class MessageCodecRegistry
{
    private const string ChatRoute = "chat";
    private const string ClipboardRoute = "clipboard";

    public bool TryEncode(AppMessage message, out DataEnvelope envelope)
    {
        return message switch
        {
            TextChatMessage text => TryEncodeTextChat(text, out envelope),
            FileChatMessage file => TryEncodeFileChat(file, out envelope),
            ImageChatMessage image => TryEncodeImageChat(image, out envelope),
            FileOfferChatMessage offer => TryEncodeFileOffer(offer, out envelope),
            FileOfferReceivedChatMessage offerReceived => TryEncodeTransferControl(offerReceived, "file.offer.received", offerReceived.TransferId, null, out envelope),
            FileAcceptChatMessage accept => TryEncodeTransferControl(accept, "file.accept", accept.TransferId, null, out envelope),
            FileRejectChatMessage reject => TryEncodeTransferControl(reject, "file.reject", reject.TransferId, reject.Reason, out envelope),
            FileCancelChatMessage cancel => TryEncodeTransferControl(cancel, "file.cancel", cancel.TransferId, cancel.Reason, out envelope),
            FileCompleteChatMessage complete => TryEncodeTransferControl(complete, "file.complete", complete.TransferId, null, out envelope),
            TextClipboardMessage clipboard => TryEncodeTextClipboard(clipboard, out envelope),
            _ => Fail(out envelope)
        };
    }

    public bool TryDecode(DataEnvelope envelope, out AppMessage message)
    {
        return (envelope.Route, envelope.Command) switch
        {
            (ChatRoute, "text") => TryDecodeTextChat(envelope, out message),
            (ChatRoute, "file") => TryDecodeFileChat(envelope, out message),
            (ChatRoute, "image.direct") => TryDecodeImageChat(envelope, out message),
            (ChatRoute, "file.offer") => TryDecodeFileOffer(envelope, out message),
            (ChatRoute, "file.offer.received") => TryDecodeTransferControl(envelope, command: "file.offer.received", out message),
            (ChatRoute, "file.accept") => TryDecodeTransferControl(envelope, command: "file.accept", out message),
            (ChatRoute, "file.reject") => TryDecodeTransferControl(envelope, command: "file.reject", out message),
            (ChatRoute, "file.cancel") => TryDecodeTransferControl(envelope, command: "file.cancel", out message),
            (ChatRoute, "file.complete") => TryDecodeTransferControl(envelope, command: "file.complete", out message),
            (ClipboardRoute, "text") => TryDecodeTextClipboard(envelope, out message),
            _ => Fail(out message)
        };
    }

    private static bool TryEncodeTextChat(TextChatMessage message, out DataEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(message.Text))
        {
            return Fail(out envelope);
        }

        envelope = CreateEnvelope(ChatRoute, "text", DataStreamType.Text, Guid.Empty, "text/plain", message.ConversationId,
            metadata => metadata["text"] = message.Text);
        return true;
    }

    private static bool TryEncodeFileChat(FileChatMessage message, out DataEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(message.FileName))
        {
            return Fail(out envelope);
        }

        envelope = CreateEnvelope(ChatRoute, "file", DataStreamType.File, message.ChannelId, "application/octet-stream", message.ConversationId,
            metadata =>
            {
                metadata["fileName"] = message.FileName;
                metadata["length"] = message.Length?.ToString();
            });
        return true;
    }

    private static bool TryEncodeImageChat(ImageChatMessage message, out DataEnvelope envelope)
    {
        if (!message.IsDirect)
        {
            return Fail(out envelope);
        }

        envelope = CreateEnvelope(ChatRoute, "image.direct", DataStreamType.Image, message.TransferId, message.ContentType, message.ConversationId,
            metadata => metadata["sizeBytes"] = message.SizeBytes.ToString());
        return true;
    }

    private static bool TryEncodeFileOffer(FileOfferChatMessage message, out DataEnvelope envelope)
    {
        envelope = CreateEnvelope(ChatRoute, "file.offer", DataStreamType.File, message.TransferId, message.ContentType, message.ConversationId,
            metadata =>
            {
                metadata["fileName"] = message.FileName;
                metadata["sizeBytes"] = message.SizeBytes.ToString();
                metadata["hash"] = message.Hash;
                if (message.IconPng is { Length: > 0 })
                    metadata["iconPng"] = Convert.ToBase64String(message.IconPng);
            });
        return true;
    }

    private static bool TryEncodeTransferControl(
        AppMessage message,
        string command,
        Guid transferId,
        string? reason,
        out DataEnvelope envelope)
    {
        envelope = CreateEnvelope(ChatRoute, command, DataStreamType.Control, transferId, null, message.ConversationId,
            metadata =>
            {
                if (reason is not null)
                {
                    metadata["reason"] = reason;
                }
            });
        return true;
    }

    private static bool TryEncodeTextClipboard(TextClipboardMessage message, out DataEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(message.Text))
        {
            return Fail(out envelope);
        }

        envelope = CreateEnvelope(ClipboardRoute, "text", DataStreamType.Text, Guid.Empty, "text/plain", message.ConversationId,
            metadata => metadata["text"] = message.Text,
            includeSenderId: false);
        return true;
    }

    private static bool TryDecodeTextChat(DataEnvelope envelope, out AppMessage message)
    {
        message = null!;
        var conversationId = GetPeerId(envelope);
        var text = GetMetadata(envelope, "text");
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        message = new TextChatMessage(conversationId, text);
        return true;
    }

    private static bool TryDecodeFileChat(DataEnvelope envelope, out AppMessage message)
    {
        message = null!;
        var conversationId = GetPeerId(envelope);
        var fileName = GetMetadata(envelope, "fileName");
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var lengthText = GetMetadata(envelope, "length");
        long? length = !string.IsNullOrWhiteSpace(lengthText) && long.TryParse(lengthText, out var parsedLength)
            ? parsedLength
            : null;
        message = new FileChatMessage(conversationId, envelope.ChannelId, fileName, length);
        return true;
    }

    private static bool TryDecodeImageChat(DataEnvelope envelope, out AppMessage message)
    {
        message = null!;
        var conversationId = GetPeerId(envelope);
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return false;
        }

        TryParseLong(GetMetadata(envelope, "sizeBytes"), out var sizeBytes);
        message = new ImageChatMessage(conversationId, envelope.ChannelId, sizeBytes, envelope.ContentType, true);
        return true;
    }

    private static bool TryDecodeFileOffer(DataEnvelope envelope, out AppMessage message)
    {
        message = null!;
        var conversationId = GetPeerId(envelope);
        var fileName = GetMetadata(envelope, "fileName");
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        TryParseLong(GetMetadata(envelope, "sizeBytes"), out var sizeBytes);
        var iconPngStr = GetMetadata(envelope, "iconPng");
        byte[]? iconPng = null;
        if (!string.IsNullOrWhiteSpace(iconPngStr))
        {
            try { iconPng = Convert.FromBase64String(iconPngStr); }
            catch { /* ignore invalid icon data */ }
        }

        message = new FileOfferChatMessage(
            conversationId,
            envelope.ChannelId,
            fileName,
            sizeBytes,
            envelope.ContentType,
            GetMetadata(envelope, "hash"),
            iconPng);
        return true;
    }

    private static bool TryDecodeTransferControl(DataEnvelope envelope, string command, out AppMessage message)
    {
        message = null!;
        var conversationId = GetPeerId(envelope);
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return false;
        }

        message = command switch
        {
            "file.accept" => new FileAcceptChatMessage(conversationId, envelope.ChannelId),
            "file.offer.received" => new FileOfferReceivedChatMessage(conversationId, envelope.ChannelId),
            "file.reject" => new FileRejectChatMessage(conversationId, envelope.ChannelId, GetMetadata(envelope, "reason") ?? string.Empty),
            "file.cancel" => new FileCancelChatMessage(conversationId, envelope.ChannelId, GetMetadata(envelope, "reason") ?? string.Empty),
            "file.complete" => new FileCompleteChatMessage(conversationId, envelope.ChannelId),
            _ => null!
        };
        return message is not null;
    }

    private static bool TryDecodeTextClipboard(DataEnvelope envelope, out AppMessage message)
    {
        message = null!;
        var conversationId = GetMetadata(envelope, "conversationId");
        var text = GetMetadata(envelope, "text");
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        message = new TextClipboardMessage(conversationId, text);
        return true;
    }

    private static DataEnvelope CreateEnvelope(
        string route,
        string command,
        DataStreamType streamType,
        Guid channelId,
        string? contentType,
        string conversationId,
        Action<Dictionary<string, string?>>? configureMetadata = null,
        bool includeSenderId = true)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["conversationId"] = conversationId
        };

        if (includeSenderId)
        {
            metadata["senderId"] = ResolveSenderId();
        }

        configureMetadata?.Invoke(metadata);

        return new DataEnvelope
        {
            Route = route,
            Command = command,
            StreamType = streamType,
            ChannelId = channelId,
            Sequence = 0,
            ContentType = contentType,
            Metadata = metadata
        };
    }

    private static string? GetMetadata(DataEnvelope envelope, string key)
    {
        return envelope.Metadata?.TryGetValue(key, out var value) == true ? value : null;
    }

    private static string? GetPeerId(DataEnvelope envelope)
    {
        var senderId = GetMetadata(envelope, "senderId");
        return string.IsNullOrWhiteSpace(senderId) ? GetMetadata(envelope, "conversationId") : senderId;
    }

    private static bool TryParseLong(string? value, out long result)
    {
        return long.TryParse(string.IsNullOrWhiteSpace(value) ? "0" : value, out result);
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

    private static bool Fail(out DataEnvelope envelope)
    {
        envelope = new DataEnvelope();
        return false;
    }

    private static bool Fail(out AppMessage message)
    {
        message = null!;
        return false;
    }
}
