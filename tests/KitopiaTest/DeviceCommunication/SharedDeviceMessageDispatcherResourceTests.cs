using System.IO.Pipelines;
using Kitopia.Feature.DeviceCommunication;
using Kitopia.Feature.DeviceCommunication.Application;
using Kitopia.Feature.DeviceCommunication.Codecs;
using Kitopia.Feature.DeviceCommunication.Messages;
using Kitopia.Feature.DeviceCommunication.Protocol;
using Kitopia.Feature.DeviceCommunication.Routing;
using Kitopia.Feature.DeviceCommunication.Sessions;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class SharedDeviceMessageDispatcherResourceTests
{
    [TestMethod]
    public async Task DispatchAsync_DirectImageWithMatchingSize_PublishesPayload()
    {
        var sink = new RecordingSink();
        var dispatcher = CreateDispatcher(sink);
        var bytes = new byte[] { 1, 2, 3 };

        await dispatcher.DispatchAsync(CreateImageEnvelope(bytes.Length), PipeReader.Create(new MemoryStream(bytes)));

        var image = sink.Events.OfType<ChatMessageReceivedEvent>().Single();
        CollectionAssert.AreEqual(bytes, image.PayloadBytes);
    }

    [TestMethod]
    public async Task DispatchAsync_DirectImageExceedsDeclaredSize_RejectsBeforePublishing()
    {
        var sink = new RecordingSink();
        var dispatcher = CreateDispatcher(sink);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await dispatcher.DispatchAsync(
            CreateImageEnvelope(3),
            PipeReader.Create(new MemoryStream(new byte[] { 1, 2, 3, 4 }))));

        Assert.AreEqual(0, sink.Events.Count);
    }

    [TestMethod]
    public async Task DispatchAsync_DirectImageExceedsLimit_RejectsBeforeReadingPayload()
    {
        var sink = new RecordingSink();
        var dispatcher = CreateDispatcher(sink);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await dispatcher.DispatchAsync(
            CreateImageEnvelope(DeviceMessageDispatcher.MaximumDirectImageBytes + 1),
            PipeReader.Create(new MemoryStream())));

        Assert.AreEqual(0, sink.Events.Count);
    }

    private static DeviceMessageDispatcher CreateDispatcher(RecordingSink sink)
    {
        return new DeviceMessageDispatcher(new MessageCodecRegistry(), sink,
            new FileTransferPayloadHandler(sink, new FileTransferSessionStore()));
    }

    private static DataEnvelope CreateImageEnvelope(long sizeBytes)
    {
        return new DataEnvelope
        {
            Route = "chat",
            Command = "image.direct",
            StreamType = DataStreamType.Image,
            ChannelId = Guid.NewGuid(),
            ContentType = "image/png",
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["senderId"] = "peer-1",
                ["conversationId"] = "receiver",
                ["sizeBytes"] = sizeBytes.ToString()
            }
        };
    }

    private sealed class RecordingSink : IIncomingMessageSink
    {
        public List<DeviceMessageEvent> Events { get; } = [];

        public ValueTask PublishAsync(AppMessage message, CancellationToken cancellationToken = default)
        {
            Events.Add(DeviceMessageEventFactory.FromMessage(message));
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishEventAsync(DeviceMessageEvent messageEvent,
            CancellationToken cancellationToken = default)
        {
            Events.Add(messageEvent);
            return ValueTask.CompletedTask;
        }
    }
}
