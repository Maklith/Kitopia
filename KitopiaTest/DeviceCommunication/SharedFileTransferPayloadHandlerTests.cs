using System.IO.Pipelines;
using Kitopia.DeviceCommunication.Application;
using Kitopia.DeviceCommunication.Messages.Chat;
using Kitopia.DeviceCommunication.Sessions;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class SharedFileTransferPayloadHandlerTests
{
    [TestMethod]
    public async Task HandleAsync_AcceptedSession_SavesFileAndPublishesCompleted()
    {
        var sink = new RecordingSink();
        var store = new FileTransferSessionStore();
        var handler = new FileTransferPayloadHandler(sink, store);
        var transferId = Guid.NewGuid();
        var tempFile = Path.Combine(Path.GetTempPath(), $"kitopia-shared-{transferId:D}.bin");
        var payloadBytes = new byte[] { 1, 2, 3, 4 };

        store.TryAdd(new FileTransferSession
        {
            ConversationId = "peer-1",
            TransferId = transferId,
            FileName = "shared.bin",
            SizeBytes = payloadBytes.Length,
            State = FileTransferState.Accepted,
            SavePath = tempFile
        });

        try
        {
            await handler.HandleAsync(
                new FileChatMessage("peer-1", transferId, "shared.bin", payloadBytes.Length),
                PipeReader.Create(new MemoryStream(payloadBytes)),
                CancellationToken.None);

            Assert.IsTrue(File.Exists(tempFile));
            CollectionAssert.AreEqual(payloadBytes, await File.ReadAllBytesAsync(tempFile));
            Assert.IsFalse(store.TryGet(transferId, out _));

            var completed = sink.Events.OfType<FileTransferUpdatedEvent>()
                .FirstOrDefault(evt => evt.Status == FileTransferStatus.Completed);
            Assert.IsNotNull(completed);
            Assert.AreEqual(FileTransferDirection.Download, completed.Direction);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [TestMethod]
    public async Task HandleAsync_AcceptedSessionWithWriteStream_SavesPayloadDirectly()
    {
        var sink = new RecordingSink();
        var store = new FileTransferSessionStore();
        var handler = new FileTransferPayloadHandler(sink, store);
        var transferId = Guid.NewGuid();
        var payloadBytes = new byte[] { 5, 6, 7, 8 };
        await using var target = new MemoryStream();

        store.TryAdd(new FileTransferSession
        {
            ConversationId = "peer-1",
            TransferId = transferId,
            FileName = "shared.bin",
            SizeBytes = payloadBytes.Length,
            State = FileTransferState.Accepted,
            SavePath = "content://kitopia/shared.bin",
            OpenWriteStreamAsync = _ => new ValueTask<Stream>(target)
        });

        await handler.HandleAsync(
            new FileChatMessage("peer-1", transferId, "shared.bin", payloadBytes.Length),
            PipeReader.Create(new MemoryStream(payloadBytes)),
            CancellationToken.None);

        CollectionAssert.AreEqual(payloadBytes, target.ToArray());
        var completed = sink.Events.OfType<FileTransferUpdatedEvent>()
            .FirstOrDefault(evt => evt.Status == FileTransferStatus.Completed);
        Assert.IsNotNull(completed);
        Assert.AreEqual(payloadBytes.LongLength, completed.BytesTransferred);
    }

    [TestMethod]
    public async Task HandleAsync_MissingAcceptedSession_DrainsPayloadAndPublishesFailed()
    {
        var sink = new RecordingSink();
        var handler = new FileTransferPayloadHandler(sink, new FileTransferSessionStore());

        await handler.HandleAsync(
            new FileChatMessage("peer-1", Guid.NewGuid(), "shared.bin", 4),
            PipeReader.Create(new MemoryStream(new byte[] { 1, 2, 3, 4 })),
            CancellationToken.None);

        var failed = sink.Events.OfType<FileTransferUpdatedEvent>()
            .FirstOrDefault(evt => evt.Status == FileTransferStatus.Failed);
        Assert.IsNotNull(failed);
        Assert.AreEqual("missing_accept_session", failed.Reason);
    }

    private sealed class RecordingSink : IIncomingMessageSink
    {
        public List<DeviceMessageEvent> Events { get; } = [];

        public ValueTask PublishAsync(
            Kitopia.DeviceCommunication.Messages.AppMessage message,
            CancellationToken cancellationToken = default)
        {
            Events.Add(DeviceMessageEventFactory.FromMessage(message));
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishEventAsync(
            DeviceMessageEvent messageEvent,
            CancellationToken cancellationToken = default)
        {
            Events.Add(messageEvent);
            return ValueTask.CompletedTask;
        }
    }
}
