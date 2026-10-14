using System.Text.Json;
using Kitopia.Feature.DeviceCommunication.Discovery;
using Kitopia.Feature.DeviceCommunication.Protocol;
using Kitopia.Feature.DeviceCommunication.Routing;
using Kitopia.Feature.DeviceCommunication.Serialization;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class SourceGeneratedJsonTests
{
    [TestMethod]
    public void DataEnvelopeContext_DeserializesLegacyWireJsonAndPreservesPropertyNames()
    {
        const string legacyJson =
            """
            {
              "Route": "chat",
              "Command": "file.offer",
              "StreamType": 3,
              "ChannelId": "f559b65f-0487-44cc-b789-7f533e7fbd5e",
              "Sequence": 7,
              "ContentType": "application/pdf",
              "Metadata": {
                "senderId": "legacy-peer",
                "optional": null
              }
            }
            """;

        var envelope = JsonSerializer.Deserialize(
            legacyJson,
            DeviceCommunicationJsonSerializerContext.Default.DataEnvelope);

        Assert.IsNotNull(envelope);
        Assert.AreEqual("chat", envelope.Route);
        Assert.AreEqual("file.offer", envelope.Command);
        Assert.AreEqual(DataStreamType.File, envelope.StreamType);
        Assert.AreEqual(Guid.Parse("f559b65f-0487-44cc-b789-7f533e7fbd5e"), envelope.ChannelId);
        Assert.AreEqual(7, envelope.Sequence);
        Assert.AreEqual("legacy-peer", envelope.Metadata?["senderId"]);
        Assert.IsNull(envelope.Metadata?["optional"]);

        var serialized = JsonSerializer.Serialize(
            envelope,
            DeviceCommunicationJsonSerializerContext.Default.DataEnvelope);
        using var document = JsonDocument.Parse(serialized);
        var root = document.RootElement;
        Assert.AreEqual("chat", root.GetProperty("Route").GetString());
        Assert.AreEqual("file.offer", root.GetProperty("Command").GetString());
        Assert.AreEqual(3, root.GetProperty("StreamType").GetInt32());
        Assert.IsFalse(root.TryGetProperty("route", out _));
    }

    [TestMethod]
    public void DiscoveryInfoContext_PreservesLegacyWireShape()
    {
        var info = new DiscoveryInfo
        {
            MessageType = "auth.response",
            Version = "0.1",
            Id = "device-id",
            Name = "Pixel",
            OperatingSystem = "Android",
            TcpPort = 22001,
            TimestampUnixSeconds = 123456,
            Signature = "signature",
            PublicKey = "public-key",
            Nonce = "nonce"
        };

        var serialized = JsonSerializer.Serialize(
            info,
            DeviceCommunicationJsonSerializerContext.Default.DiscoveryInfo);
        var deserialized = JsonSerializer.Deserialize(
            serialized,
            DeviceCommunicationJsonSerializerContext.Default.DiscoveryInfo);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual(info.MessageType, deserialized.MessageType);
        Assert.AreEqual(info.OperatingSystem, deserialized.OperatingSystem);
        Assert.AreEqual(info.TcpPort, deserialized.TcpPort);
        using var document = JsonDocument.Parse(serialized);
        Assert.AreEqual("auth.response", document.RootElement.GetProperty("MessageType").GetString());
        Assert.AreEqual("Android", document.RootElement.GetProperty("OperatingSystem").GetString());
        Assert.IsFalse(document.RootElement.TryGetProperty("messageType", out _));
    }
}
