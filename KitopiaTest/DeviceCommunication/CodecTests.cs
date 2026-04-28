using System.Text.Json;
using Core.Services.DeviceCommunication;
using Core.Services.DeviceCommunication.Codecs;
using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Messages.Clipboard;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Routing;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class CodecTests
{
    #region ChatMessageCodec

    [TestMethod]
    public void ChatMessageCodec_Encode_ProducesCorrectEnvelope()
    {
        var codec = new ChatMessageCodec();
        var message = new TextChatMessage("peer-1", "hello world");

        var ok = codec.TryEncode(message, out var envelope);

        Assert.IsTrue(ok);
        Assert.AreEqual("chat", envelope.Route);
        Assert.AreEqual("text", envelope.Command);
        Assert.AreEqual(DataStreamType.Text, envelope.StreamType);
        Assert.AreEqual("text/plain", envelope.ContentType);
        Assert.AreEqual("peer-1", envelope.Metadata!["conversationId"]);
        Assert.AreEqual("hello world", envelope.Metadata["text"]);
    }

    [TestMethod]
    public void ChatMessageCodec_Encode_ReturnsFalse_WhenTextEmpty()
    {
        var codec = new ChatMessageCodec();
        var message = new TextChatMessage("peer-1", "");

        var ok = codec.TryEncode(message, out _);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void ChatMessageCodec_Decode_RoundTrips()
    {
        var codec = new ChatMessageCodec();
        var original = new TextChatMessage("peer-1", "test message");

        codec.TryEncode(original, out var envelope);
        var ok = codec.TryDecode(envelope, out var decoded);

        Assert.IsTrue(ok);
        Assert.IsInstanceOfType<TextChatMessage>(decoded);
        Assert.AreEqual("test message", ((TextChatMessage)decoded).Text);
    }

    [TestMethod]
    public void ChatMessageCodec_Decode_ReturnsFalse_ForWrongRoute()
    {
        var codec = new ChatMessageCodec();
        var envelope = new DataEnvelope
        {
            Route = "clipboard", Command = "text", StreamType = DataStreamType.Text,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "peer-1", ["text"] = "hi"
            }
        };

        var ok = codec.TryDecode(envelope, out _);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void ChatMessageCodec_Decode_ReturnsFalse_ForWrongCommand()
    {
        var codec = new ChatMessageCodec();
        var envelope = new DataEnvelope
        {
            Route = "chat", Command = "file", StreamType = DataStreamType.Text,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "peer-1", ["text"] = "hi"
            }
        };

        var ok = codec.TryDecode(envelope, out _);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void ChatMessageCodec_Decode_ReturnsFalse_WhenMetadataMissing()
    {
        var codec = new ChatMessageCodec();
        var envelope = new DataEnvelope { Route = "chat", Command = "text", StreamType = DataStreamType.Text };

        var ok = codec.TryDecode(envelope, out _);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void ChatMessageCodec_TryEncode_ReturnsFalse_ForWrongMessageType()
    {
        var codec = new ChatMessageCodec();
        var message = new FileChatMessage("peer-1", Guid.NewGuid(), "file.txt", 100);

        var ok = codec.TryEncode(message, out _);

        Assert.IsFalse(ok);
    }

    #endregion

    #region FileChatMessageCodec

    [TestMethod]
    public void FileChatMessageCodec_Encode_ProducesCorrectEnvelope()
    {
        var codec = new FileChatMessageCodec();
        var channelId = Guid.NewGuid();
        var message = new FileChatMessage("peer-1", channelId, "test.bin", 1024);

        var ok = codec.TryEncode(message, out var envelope);

        Assert.IsTrue(ok);
        Assert.AreEqual("chat", envelope.Route);
        Assert.AreEqual("file", envelope.Command);
        Assert.AreEqual(DataStreamType.File, envelope.StreamType);
        Assert.AreEqual(channelId, envelope.ChannelId);
        Assert.AreEqual("test.bin", envelope.Metadata!["fileName"]);
        Assert.AreEqual("1024", envelope.Metadata["length"]);
    }

    [TestMethod]
    public void FileChatMessageCodec_Encode_ReturnsFalse_WhenFileNameEmpty()
    {
        var codec = new FileChatMessageCodec();
        var message = new FileChatMessage("peer-1", Guid.NewGuid(), "", 100);

        var ok = codec.TryEncode(message, out _);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void FileChatMessageCodec_Decode_RoundTrips()
    {
        var codec = new FileChatMessageCodec();
        var original = new FileChatMessage("peer-1", Guid.NewGuid(), "data.zip", 9999);

        codec.TryEncode(original, out var envelope);
        var ok = codec.TryDecode(envelope, out var decoded);

        Assert.IsTrue(ok);
        Assert.IsInstanceOfType<FileChatMessage>(decoded);
        var file = (FileChatMessage)decoded;
        Assert.AreEqual("data.zip", file.FileName);
        Assert.AreEqual(9999, file.Length);
    }

    [TestMethod]
    public void FileChatMessageCodec_Decode_HandlesNullLength()
    {
        var codec = new FileChatMessageCodec();
        var envelope = new DataEnvelope
        {
            Route = "chat", Command = "file", StreamType = DataStreamType.File,
            ChannelId = Guid.NewGuid(),
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "peer-1", ["fileName"] = "no-length.bin"
            }
        };

        var ok = codec.TryDecode(envelope, out var decoded);

        Assert.IsTrue(ok);
        Assert.IsNull(((FileChatMessage)decoded).Length);
    }

    #endregion

    #region ImageChatMessageCodec

    [TestMethod]
    public void ImageChatMessageCodec_Encode_ProducesCorrectEnvelope()
    {
        var codec = new ImageChatMessageCodec();
        var transferId = Guid.NewGuid();
        var message = new ImageChatMessage("peer-1", transferId, 5000, "image/png", true);

        var ok = codec.TryEncode(message, out var envelope);

        Assert.IsTrue(ok);
        Assert.AreEqual("chat", envelope.Route);
        Assert.AreEqual("image.direct", envelope.Command);
        Assert.AreEqual(DataStreamType.Image, envelope.StreamType);
        Assert.AreEqual(transferId, envelope.ChannelId);
        Assert.AreEqual("image/png", envelope.ContentType);
        Assert.AreEqual("5000", envelope.Metadata!["sizeBytes"]);
    }

    [TestMethod]
    public void ImageChatMessageCodec_Encode_ReturnsFalse_WhenNotDirect()
    {
        var codec = new ImageChatMessageCodec();
        var message = new ImageChatMessage("peer-1", Guid.NewGuid(), 5000, "image/png", false);

        var ok = codec.TryEncode(message, out _);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void ImageChatMessageCodec_Decode_RoundTrips()
    {
        var codec = new ImageChatMessageCodec();
        var original = new ImageChatMessage("peer-1", Guid.NewGuid(), 5000, "image/png", true);

        codec.TryEncode(original, out var envelope);
        var ok = codec.TryDecode(envelope, out var decoded);

        Assert.IsTrue(ok);
        Assert.IsInstanceOfType<ImageChatMessage>(decoded);
        var image = (ImageChatMessage)decoded;
        Assert.AreEqual(5000, image.SizeBytes);
        Assert.IsTrue(image.IsDirect);
    }

    #endregion

    #region FileOfferChatMessageCodec

    [TestMethod]
    public void FileOfferChatMessageCodec_Encode_ProducesCorrectEnvelope()
    {
        var codec = new FileOfferChatMessageCodec();
        var transferId = Guid.NewGuid();
        var message = new FileOfferChatMessage("peer-1", transferId, "doc.pdf", 2048, "application/pdf", "abc123");

        var ok = codec.TryEncode(message, out var envelope);

        Assert.IsTrue(ok);
        Assert.AreEqual("chat", envelope.Route);
        Assert.AreEqual("file.offer", envelope.Command);
        Assert.AreEqual(DataStreamType.File, envelope.StreamType);
        Assert.AreEqual(transferId, envelope.ChannelId);
        Assert.AreEqual("doc.pdf", envelope.Metadata!["fileName"]);
        Assert.AreEqual("2048", envelope.Metadata["sizeBytes"]);
        Assert.AreEqual("abc123", envelope.Metadata["hash"]);
    }

    [TestMethod]
    public void FileOfferChatMessageCodec_Decode_RoundTrips()
    {
        var codec = new FileOfferChatMessageCodec();
        var original = new FileOfferChatMessage("peer-1", Guid.NewGuid(), "doc.pdf", 2048, "application/pdf", "hash1");

        codec.TryEncode(original, out var envelope);
        var ok = codec.TryDecode(envelope, out var decoded);

        Assert.IsTrue(ok);
        Assert.IsInstanceOfType<FileOfferChatMessage>(decoded);
        var offer = (FileOfferChatMessage)decoded;
        Assert.AreEqual("doc.pdf", offer.FileName);
        Assert.AreEqual(2048, offer.SizeBytes);
        Assert.AreEqual("hash1", offer.Hash);
    }

    #endregion

    #region FileAcceptChatMessageCodec

    [TestMethod]
    public void FileAcceptChatMessageCodec_EncodeDecode_RoundTrips()
    {
        var codec = new FileAcceptChatMessageCodec();
        var transferId = Guid.NewGuid();
        var original = new FileAcceptChatMessage("peer-1", transferId);

        var encodeOk = codec.TryEncode(original, out var envelope);
        Assert.IsTrue(encodeOk);
        Assert.AreEqual("file.accept", envelope.Command);
        Assert.AreEqual(DataStreamType.Control, envelope.StreamType);
        Assert.AreEqual(transferId, envelope.ChannelId);

        var decodeOk = codec.TryDecode(envelope, out var decoded);
        Assert.IsTrue(decodeOk);
        Assert.IsInstanceOfType<FileAcceptChatMessage>(decoded);
        Assert.AreEqual(transferId, ((FileAcceptChatMessage)decoded).TransferId);
    }

    #endregion

    #region FileRejectChatMessageCodec

    [TestMethod]
    public void FileRejectChatMessageCodec_EncodeDecode_RoundTrips()
    {
        var codec = new FileRejectChatMessageCodec();
        var transferId = Guid.NewGuid();
        var original = new FileRejectChatMessage("peer-1", transferId, "rejected_by_user");

        var encodeOk = codec.TryEncode(original, out var envelope);
        Assert.IsTrue(encodeOk);
        Assert.AreEqual("file.reject", envelope.Command);
        Assert.AreEqual(DataStreamType.Control, envelope.StreamType);
        Assert.AreEqual("rejected_by_user", envelope.Metadata!["reason"]);

        var decodeOk = codec.TryDecode(envelope, out var decoded);
        Assert.IsTrue(decodeOk);
        Assert.IsInstanceOfType<FileRejectChatMessage>(decoded);
        Assert.AreEqual("rejected_by_user", ((FileRejectChatMessage)decoded).Reason);
    }

    #endregion

    #region FileCancelChatMessageCodec

    [TestMethod]
    public void FileCancelChatMessageCodec_EncodeDecode_RoundTrips()
    {
        var codec = new FileCancelChatMessageCodec();
        var transferId = Guid.NewGuid();
        var original = new FileCancelChatMessage("peer-1", transferId, "user_cancelled");

        var encodeOk = codec.TryEncode(original, out var envelope);
        Assert.IsTrue(encodeOk);
        Assert.AreEqual("file.cancel", envelope.Command);
        Assert.AreEqual(DataStreamType.Control, envelope.StreamType);
        Assert.AreEqual("user_cancelled", envelope.Metadata!["reason"]);

        var decodeOk = codec.TryDecode(envelope, out var decoded);
        Assert.IsTrue(decodeOk);
        Assert.IsInstanceOfType<FileCancelChatMessage>(decoded);
        Assert.AreEqual("user_cancelled", ((FileCancelChatMessage)decoded).Reason);
    }

    #endregion

    #region FileCompleteChatMessageCodec

    [TestMethod]
    public void FileCompleteChatMessageCodec_EncodeDecode_RoundTrips()
    {
        var codec = new FileCompleteChatMessageCodec();
        var transferId = Guid.NewGuid();
        var original = new FileCompleteChatMessage("peer-1", transferId);

        var encodeOk = codec.TryEncode(original, out var envelope);
        Assert.IsTrue(encodeOk);
        Assert.AreEqual("file.complete", envelope.Command);
        Assert.AreEqual(DataStreamType.Control, envelope.StreamType);
        Assert.AreEqual(transferId, envelope.ChannelId);

        var decodeOk = codec.TryDecode(envelope, out var decoded);
        Assert.IsTrue(decodeOk);
        Assert.IsInstanceOfType<FileCompleteChatMessage>(decoded);
        Assert.AreEqual(transferId, ((FileCompleteChatMessage)decoded).TransferId);
    }

    #endregion

    #region ClipboardMessageCodec

    [TestMethod]
    public void ClipboardMessageCodec_Encode_ProducesCorrectEnvelope()
    {
        var codec = new ClipboardMessageCodec();
        var message = new TextClipboardMessage("peer-1", "clipboard text");

        var ok = codec.TryEncode(message, out var envelope);

        Assert.IsTrue(ok);
        Assert.AreEqual("clipboard", envelope.Route);
        Assert.AreEqual("text", envelope.Command);
        Assert.AreEqual(DataStreamType.Text, envelope.StreamType);
        Assert.AreEqual("clipboard text", envelope.Metadata!["text"]);
    }

    [TestMethod]
    public void ClipboardMessageCodec_Encode_ReturnsFalse_WhenTextEmpty()
    {
        var codec = new ClipboardMessageCodec();
        var message = new TextClipboardMessage("peer-1", "");

        var ok = codec.TryEncode(message, out _);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void ClipboardMessageCodec_Decode_RoundTrips()
    {
        var codec = new ClipboardMessageCodec();
        var original = new TextClipboardMessage("peer-1", "copied text");

        codec.TryEncode(original, out var envelope);
        var ok = codec.TryDecode(envelope, out var decoded);

        Assert.IsTrue(ok);
        Assert.IsInstanceOfType<TextClipboardMessage>(decoded);
        Assert.AreEqual("copied text", ((TextClipboardMessage)decoded).Text);
    }

    [TestMethod]
    public void ClipboardMessageCodec_Decode_ReturnsFalse_ForWrongRoute()
    {
        var codec = new ClipboardMessageCodec();
        var envelope = new DataEnvelope
        {
            Route = "chat", Command = "text", StreamType = DataStreamType.Text,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "peer-1", ["text"] = "hi"
            }
        };

        var ok = codec.TryDecode(envelope, out _);

        Assert.IsFalse(ok);
    }

    #endregion

    #region MessageCodecRegistry

    [TestMethod]
    public void Registry_Constructor_Throws_WhenDuplicateRouteAndCommand()
    {
        var codecs = new IMessageCodec[] { new ChatMessageCodec(), new ChatMessageCodec() };
        Assert.ThrowsExactly<InvalidOperationException>(() => new MessageCodecRegistry(codecs));
    }

    [TestMethod]
    public void Registry_TryGetByMessage_ResolvesCorrectCodec()
    {
        var registry = new MessageCodecRegistry(new IMessageCodec[]
        {
            new ChatMessageCodec(),
            new FileChatMessageCodec(),
            new ImageChatMessageCodec(),
            new ClipboardMessageCodec()
        });

        Assert.IsTrue(registry.TryGetByMessage(new TextChatMessage("p", "t"), out var chatCodec));
        Assert.AreEqual("text", chatCodec.Command);

        Assert.IsTrue(registry.TryGetByMessage(new FileChatMessage("p", Guid.NewGuid(), "f", 1), out var fileCodec));
        Assert.AreEqual("file", fileCodec.Command);

        Assert.IsTrue(registry.TryGetByMessage(new TextClipboardMessage("p", "t"), out var clipCodec));
        Assert.AreEqual("clipboard", clipCodec.Route);
    }

    [TestMethod]
    public void Registry_TryGetByMessage_ReturnsFalse_ForUnknownType()
    {
        var registry = new MessageCodecRegistry(new IMessageCodec[] { new ChatMessageCodec() });
        var unknown = new FileCompleteChatMessage("p", Guid.NewGuid());

        var ok = registry.TryGetByMessage(unknown, out _);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void Registry_TryGetByEnvelope_ResolvesCorrectCodec()
    {
        var registry = new MessageCodecRegistry(new IMessageCodec[]
        {
            new ChatMessageCodec(),
            new FileOfferChatMessageCodec(),
            new FileAcceptChatMessageCodec()
        });

        Assert.IsTrue(registry.TryGetByEnvelope("chat", "text", out _));
        Assert.IsTrue(registry.TryGetByEnvelope("chat", "file.offer", out _));
        Assert.IsTrue(registry.TryGetByEnvelope("chat", "file.accept", out _));
        Assert.IsFalse(registry.TryGetByEnvelope("chat", "file.reject", out _));
        Assert.IsFalse(registry.TryGetByEnvelope("missing", "text", out _));
    }

    [TestMethod]
    public void Registry_AllCodecs_EncodeDecode_RoundTrip()
    {
        var codecs = new IMessageCodec[]
        {
            new ChatMessageCodec(),
            new FileChatMessageCodec(),
            new ImageChatMessageCodec(),
            new FileOfferChatMessageCodec(),
            new FileAcceptChatMessageCodec(),
            new FileRejectChatMessageCodec(),
            new FileCancelChatMessageCodec(),
            new FileCompleteChatMessageCodec(),
            new ClipboardMessageCodec()
        };
        var registry = new MessageCodecRegistry(codecs);

        foreach (var codec in codecs)
        {
            Assert.IsTrue(registry.TryGetByEnvelope(codec.Route, codec.Command, out var resolved),
                $"Registry should resolve {codec.Route}/{codec.Command}");
            Assert.AreSame(codec, resolved);
        }
    }

    #endregion
}
