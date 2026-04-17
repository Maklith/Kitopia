using Core.Services.DeviceCommunication.Codecs;
using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Routing;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class MessageCodecRegistryTests
{
    [TestMethod]
    public void Constructor_Throws_WhenDuplicateRouteAndCommand()
    {
        var codecs = new IMessageCodec[] { new ChatMessageCodec(), new ChatMessageCodec() };
        Assert.Throws<InvalidOperationException>(() => new MessageCodecRegistry(codecs));
    }

    [TestMethod]
    public void TryGetByMessage_ReturnsCodec_ForTextChatMessage()
    {
        var registry = new MessageCodecRegistry(new IMessageCodec[] { new ChatMessageCodec() });
        var message = new TextChatMessage("peer-a", "hello");

        var ok = registry.TryGetByMessage(message, out var codec);

        Assert.IsTrue(ok);
        Assert.IsNotNull(codec);
        Assert.AreEqual("chat", codec.Route);
        Assert.AreEqual("text", codec.Command);
    }

    [TestMethod]
    public void TryGetByEnvelope_ReturnsCodec_ForKnownRouteAndCommand()
    {
        var registry = new MessageCodecRegistry(new IMessageCodec[] { new ChatMessageCodec() });

        var ok = registry.TryGetByEnvelope("chat", "text", out var codec);

        Assert.IsTrue(ok);
        Assert.IsNotNull(codec);
        var encoded = codec.TryEncode(new TextChatMessage("peer-b", "payload"), out var envelope);
        Assert.IsTrue(encoded);
        Assert.AreEqual(DataStreamType.Text, envelope.StreamType);
    }
}
