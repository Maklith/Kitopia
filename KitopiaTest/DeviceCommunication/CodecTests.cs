using System.Text.Json;
using Kitopia.Feature.DeviceCommunication.Codecs;
using Kitopia.Feature.DeviceCommunication.Messages;
using Kitopia.Feature.DeviceCommunication.Messages.Chat;
using Kitopia.Feature.DeviceCommunication.Messages.Clipboard;
using Kitopia.Feature.DeviceCommunication.Protocol;
using Kitopia.Feature.DeviceCommunication.Routing;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class CodecTests
{
    [TestMethod]
    public void Registry_DecodesLegacyCoreFileOfferEnvelope()
    {
        const string legacyEnvelopeJson =
            """
            {
              "Route": "chat",
              "Command": "file.offer",
              "StreamType": 3,
              "ChannelId": "f559b65f-0487-44cc-b789-7f533e7fbd5e",
              "Sequence": 0,
              "ContentType": "application/pdf",
              "Metadata": {
                "conversationId": "local-conversation",
                "senderId": "legacy-peer",
                "fileName": "document.pdf",
                "sizeBytes": "512",
                "hash": "sha256-value",
                "iconPng": "iVBORw=="
              }
            }
            """;
        var envelope = JsonSerializer.Deserialize<DataEnvelope>(legacyEnvelopeJson);
        var registry = new MessageCodecRegistry();

        Assert.IsNotNull(envelope);
        Assert.IsTrue(registry.TryDecode(envelope, out var decoded));
        Assert.IsInstanceOfType<FileOfferChatMessage>(decoded);

        var offer = (FileOfferChatMessage)decoded;
        Assert.AreEqual("legacy-peer", offer.ConversationId);
        Assert.AreEqual(Guid.Parse("f559b65f-0487-44cc-b789-7f533e7fbd5e"), offer.TransferId);
        Assert.AreEqual("document.pdf", offer.FileName);
        Assert.AreEqual(512, offer.SizeBytes);
        Assert.AreEqual("application/pdf", offer.ContentType);
        Assert.AreEqual("sha256-value", offer.Hash);
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47 }, offer.IconPng);
    }

    [TestMethod]
    public void Registry_Encode_ProducesExpectedEnvelope_ForTextChat()
    {
        var registry = new MessageCodecRegistry();
        var message = new TextChatMessage("peer-1", "hello world");

        var ok = registry.TryEncode(message, out var envelope);

        Assert.IsTrue(ok);
        Assert.AreEqual("chat", envelope.Route);
        Assert.AreEqual("text", envelope.Command);
        Assert.AreEqual(DataStreamType.Text, envelope.StreamType);
        Assert.AreEqual("text/plain", envelope.ContentType);
        Assert.AreEqual("peer-1", envelope.Metadata!["conversationId"]);
        Assert.AreEqual("hello world", envelope.Metadata["text"]);
    }

    [TestMethod]
    public void Registry_Encode_UsesInjectedIdentityProvider_ForSenderId()
    {
        var registry = new MessageCodecRegistry(new FixedIdentityProvider("sender-1"));

        var ok = registry.TryEncode(new TextChatMessage("peer-1", "hello world"), out var envelope);

        Assert.IsTrue(ok);
        Assert.AreEqual("sender-1", envelope.Metadata!["senderId"]);
    }

    [TestMethod]
    public void Registry_Encode_ReturnsFalse_WhenTextChatEmpty()
    {
        var registry = new MessageCodecRegistry();

        var ok = registry.TryEncode(new TextChatMessage("peer-1", ""), out _);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void Registry_Encode_ProducesExpectedEnvelope_ForFilePayload()
    {
        var registry = new MessageCodecRegistry();
        var channelId = Guid.NewGuid();
        var message = new FileChatMessage("peer-1", channelId, "test.bin", 1024);

        var ok = registry.TryEncode(message, out var envelope);

        Assert.IsTrue(ok);
        Assert.AreEqual("chat", envelope.Route);
        Assert.AreEqual("file", envelope.Command);
        Assert.AreEqual(DataStreamType.File, envelope.StreamType);
        Assert.AreEqual(channelId, envelope.ChannelId);
        Assert.AreEqual("application/octet-stream", envelope.ContentType);
        Assert.AreEqual("test.bin", envelope.Metadata!["fileName"]);
        Assert.AreEqual("1024", envelope.Metadata["length"]);
    }

    [TestMethod]
    public void Registry_Encode_ReturnsFalse_WhenFileNameEmpty()
    {
        var registry = new MessageCodecRegistry();

        var ok = registry.TryEncode(new FileChatMessage("peer-1", Guid.NewGuid(), "", 100), out _);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void Registry_Encode_ReturnsFalse_WhenImageIsNotDirect()
    {
        var registry = new MessageCodecRegistry();

        var ok = registry.TryEncode(new ImageChatMessage("peer-1", Guid.NewGuid(), 5000, "image/png", false), out _);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void Registry_Encode_ProducesExpectedEnvelope_ForAllControlMessages()
    {
        var registry = new MessageCodecRegistry();
        var transferId = Guid.NewGuid();

        AssertControlEnvelope(registry, new FileAcceptChatMessage("peer-1", transferId), "file.accept", transferId);
        AssertControlEnvelope(registry, new FileOfferReceivedChatMessage("peer-1", transferId), "file.offer.received", transferId);
        AssertControlEnvelope(registry, new FileRejectChatMessage("peer-1", transferId, "rejected_by_user"), "file.reject", transferId, "rejected_by_user");
        AssertControlEnvelope(registry, new FileCancelChatMessage("peer-1", transferId, "user_cancelled"), "file.cancel", transferId, "user_cancelled");
        AssertControlEnvelope(registry, new FileCompleteChatMessage("peer-1", transferId), "file.complete", transferId);
    }

    [TestMethod]
    public void Registry_EncodeDecode_RoundTrips_AllSupportedMessages()
    {
        var registry = new MessageCodecRegistry();
        var transferId = Guid.NewGuid();
        AppMessage[] messages =
        {
            new TextChatMessage("peer-1", "test message"),
            new FileChatMessage("peer-1", transferId, "data.zip", 9999),
            new ImageChatMessage("peer-1", transferId, 5000, "image/png", true),
            new FileOfferChatMessage("peer-1", transferId, "doc.pdf", 2048, "application/pdf", "hash1"),
            new FileOfferReceivedChatMessage("peer-1", transferId),
            new FileAcceptChatMessage("peer-1", transferId),
            new FileRejectChatMessage("peer-1", transferId, "rejected_by_user"),
            new FileCancelChatMessage("peer-1", transferId, "user_cancelled"),
            new FileCompleteChatMessage("peer-1", transferId),
            new TextClipboardMessage("peer-1", "copied text")
        };

        foreach (var message in messages)
        {
            Assert.IsTrue(registry.TryEncode(message, out var envelope), $"Encode failed for {message.GetType().Name}");
            Assert.IsTrue(registry.TryDecode(envelope, out var decoded), $"Decode failed for {message.GetType().Name}");
            Assert.IsInstanceOfType(decoded, message.GetType());
        }
    }

    [TestMethod]
    public void Registry_Decode_UsesSenderIdAsConversationId_WhenPresent()
    {
        var registry = new MessageCodecRegistry();
        var envelope = new DataEnvelope
        {
            Route = "chat",
            Command = "text",
            StreamType = DataStreamType.Text,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "legacy-peer",
                ["senderId"] = "sender-peer",
                ["text"] = "hi"
            }
        };

        var ok = registry.TryDecode(envelope, out var decoded);

        Assert.IsTrue(ok);
        Assert.AreEqual("sender-peer", decoded.ConversationId);
    }

    [TestMethod]
    public void Registry_Decode_ReturnsFalse_ForUnknownRouteOrCommand()
    {
        var registry = new MessageCodecRegistry();
        var envelope = new DataEnvelope
        {
            Route = "missing",
            Command = "text",
            StreamType = DataStreamType.Text,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "peer-1",
                ["text"] = "hi"
            }
        };

        var ok = registry.TryDecode(envelope, out _);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void Registry_Decode_ReturnsFalse_WhenRequiredMetadataMissing()
    {
        var registry = new MessageCodecRegistry();
        var envelope = new DataEnvelope { Route = "chat", Command = "text", StreamType = DataStreamType.Text };

        var ok = registry.TryDecode(envelope, out _);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void Registry_Decode_FilePayload_HandlesNullLength()
    {
        var registry = new MessageCodecRegistry();
        var envelope = new DataEnvelope
        {
            Route = "chat",
            Command = "file",
            StreamType = DataStreamType.File,
            ChannelId = Guid.NewGuid(),
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "peer-1",
                ["fileName"] = "no-length.bin"
            }
        };

        var ok = registry.TryDecode(envelope, out var decoded);

        Assert.IsTrue(ok);
        Assert.IsNull(((FileChatMessage)decoded).Length);
    }

    private static void AssertControlEnvelope(
        MessageCodecRegistry registry,
        AppMessage message,
        string command,
        Guid transferId,
        string? reason = null)
    {
        var ok = registry.TryEncode(message, out var envelope);

        Assert.IsTrue(ok);
        Assert.AreEqual("chat", envelope.Route);
        Assert.AreEqual(command, envelope.Command);
        Assert.AreEqual(DataStreamType.Control, envelope.StreamType);
        Assert.AreEqual(transferId, envelope.ChannelId);
        if (reason is not null)
        {
            Assert.AreEqual(reason, envelope.Metadata!["reason"]);
        }
    }

    [TestMethod]
    public void Registry_EncodeDecode_RoundTripsFileOfferIcon()
    {
        var registry = new MessageCodecRegistry();
        byte[] iconPng = [0x89, 0x50, 0x4E, 0x47];
        var offer = new FileOfferChatMessage(
            "peer-1",
            Guid.NewGuid(),
            "doc.pdf",
            2048,
            "application/pdf",
            IconPng: iconPng);

        Assert.IsTrue(registry.TryEncode(offer, out var envelope));
        Assert.IsTrue(registry.TryDecode(envelope, out var decoded));
        var decodedOffer = (FileOfferChatMessage)decoded;
        CollectionAssert.AreEqual(iconPng, decodedOffer.IconPng);
    }

    private sealed class FixedIdentityProvider : IDeviceIdentityProvider
    {
        private readonly string _publicKey;

        public FixedIdentityProvider(string publicKey)
        {
            _publicKey = publicKey;
        }

        public string? GetLocalPublicKey()
        {
            return _publicKey;
        }
    }
}
