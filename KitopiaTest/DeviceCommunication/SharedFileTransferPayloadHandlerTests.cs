using System.IO.Pipelines;
using Kitopia.Feature.DeviceCommunication.Application;
using Kitopia.Feature.DeviceCommunication.Messages.Chat;
using Kitopia.Feature.DeviceCommunication.Sessions;

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
            IsIncoming = true,
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
            IsIncoming = true,
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
    public async Task HandleAsync_MissingAcceptedSession_RejectsPayloadAndPublishesFailed()
    {
        var sink = new RecordingSink();
        var handler = new FileTransferPayloadHandler(sink, new FileTransferSessionStore());

        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await handler.HandleAsync(
            new FileChatMessage("peer-1", Guid.NewGuid(), "shared.bin", 4),
            PipeReader.Create(new MemoryStream(new byte[] { 1, 2, 3, 4 })),
            CancellationToken.None));

        var failed = sink.Events.OfType<FileTransferUpdatedEvent>()
            .FirstOrDefault(evt => evt.Status == FileTransferStatus.Failed);
        Assert.IsNotNull(failed);
        Assert.AreEqual("invalid_accept_session", failed.Reason);
    }

    [TestMethod]
    public async Task HandleAsync_WrongPeer_DoesNotOpenAcceptedTarget()
    {
        var sink = new RecordingSink();
        var store = new FileTransferSessionStore();
        var transferId = Guid.NewGuid();
        var opened = false;
        store.TryAdd(new FileTransferSession
        {
            ConversationId = "peer-1",
            TransferId = transferId,
            FileName = "shared.bin",
            SizeBytes = 4,
            IsIncoming = true,
            State = FileTransferState.Accepted,
            OpenWriteStreamAsync = _ =>
            {
                opened = true;
                return new ValueTask<Stream>(new MemoryStream());
            }
        });

        var handler = new FileTransferPayloadHandler(sink, store);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await handler.HandleAsync(
            new FileChatMessage("peer-2", transferId, "shared.bin", 4),
            PipeReader.Create(new MemoryStream(new byte[] { 1, 2, 3, 4 })),
            CancellationToken.None));

        Assert.IsFalse(opened);
        Assert.IsTrue(store.TryGet(transferId, out _));
    }

    [TestMethod]
    public async Task HandleAsync_ExceedsAcceptedSize_FailsBeforeWritingExtraBytes()
    {
        var sink = new RecordingSink();
        var store = new FileTransferSessionStore();
        var transferId = Guid.NewGuid();
        await using var target = new MemoryStream();
        store.TryAdd(new FileTransferSession
        {
            ConversationId = "peer-1",
            TransferId = transferId,
            FileName = "shared.bin",
            SizeBytes = 4,
            IsIncoming = true,
            State = FileTransferState.Accepted,
            OpenWriteStreamAsync = _ => new ValueTask<Stream>(target)
        });

        var handler = new FileTransferPayloadHandler(sink, store);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await handler.HandleAsync(
            new FileChatMessage("peer-1", transferId, "shared.bin", 4),
            PipeReader.Create(new MemoryStream(new byte[] { 1, 2, 3, 4, 5 })),
            CancellationToken.None));

        Assert.IsTrue(target.ToArray().Length <= 4);
        Assert.IsFalse(store.TryGet(transferId, out _));
        Assert.IsTrue(sink.Events.OfType<FileTransferUpdatedEvent>().Any(evt => evt.Status == FileTransferStatus.Failed));
    }

    [TestMethod]
    public async Task HandleAsync_ShortPayload_RemovesNewLocalTarget()
    {
        var sink = new RecordingSink();
        var store = new FileTransferSessionStore();
        var transferId = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), $"kitopia-incomplete-{transferId:D}.bin");
        store.TryAdd(new FileTransferSession
        {
            ConversationId = "peer-1",
            TransferId = transferId,
            FileName = "shared.bin",
            SizeBytes = 4,
            IsIncoming = true,
            State = FileTransferState.Accepted,
            SavePath = path
        });

        try
        {
            var handler = new FileTransferPayloadHandler(sink, store);
            await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await handler.HandleAsync(
                new FileChatMessage("peer-1", transferId, "shared.bin", 4),
                PipeReader.Create(new MemoryStream(new byte[] { 1, 2, 3 })),
                CancellationToken.None));

            Assert.IsFalse(File.Exists(path));
            Assert.AreEqual(0, Directory.GetFiles(Path.GetTempPath(),
                $".{Path.GetFileName(path)}.*.tmp").Length);
            Assert.IsFalse(store.TryGet(transferId, out _));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [TestMethod]
    public async Task HandleAsync_ExistingLocalTarget_ReplacesOnlyAfterCompletePayload()
    {
        var sink = new RecordingSink();
        var store = new FileTransferSessionStore();
        var transferId = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), $"kitopia-existing-{transferId:D}.bin");
        var original = new byte[] { 9, 9, 9 };
        var replacement = new byte[] { 1, 2, 3, 4 };
        await File.WriteAllBytesAsync(path, original);
        store.TryAdd(new FileTransferSession
        {
            ConversationId = "peer-1",
            TransferId = transferId,
            FileName = "shared.bin",
            SizeBytes = replacement.Length,
            IsIncoming = true,
            State = FileTransferState.Accepted,
            SavePath = path
        });

        try
        {
            var handler = new FileTransferPayloadHandler(sink, store);
            await handler.HandleAsync(new FileChatMessage("peer-1", transferId, "shared.bin", replacement.Length),
                PipeReader.Create(new MemoryStream(replacement)), CancellationToken.None);

            CollectionAssert.AreEqual(replacement, await File.ReadAllBytesAsync(path));
            Assert.AreEqual(0, Directory.GetFiles(Path.GetTempPath(),
                $".{Path.GetFileName(path)}.*.tmp").Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task HandleAsync_ShortPayload_PreservesExistingLocalTarget()
    {
        var sink = new RecordingSink();
        var store = new FileTransferSessionStore();
        var transferId = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), $"kitopia-existing-{transferId:D}.bin");
        var original = new byte[] { 9, 9, 9 };
        await File.WriteAllBytesAsync(path, original);
        store.TryAdd(new FileTransferSession
        {
            ConversationId = "peer-1",
            TransferId = transferId,
            FileName = "shared.bin",
            SizeBytes = 4,
            IsIncoming = true,
            State = FileTransferState.Accepted,
            SavePath = path
        });

        try
        {
            var handler = new FileTransferPayloadHandler(sink, store);
            await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await handler.HandleAsync(
                new FileChatMessage("peer-1", transferId, "shared.bin", 4),
                PipeReader.Create(new MemoryStream(new byte[] { 1, 2, 3 })),
                CancellationToken.None));

            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(path));
            Assert.AreEqual(0, Directory.GetFiles(Path.GetTempPath(),
                $".{Path.GetFileName(path)}.*.tmp").Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task HandleAsync_CancelledReceive_PreservesExistingLocalTarget()
    {
        var sink = new RecordingSink();
        var store = new FileTransferSessionStore();
        var transferId = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), $"kitopia-existing-{transferId:D}.bin");
        var original = new byte[] { 9, 9, 9 };
        await File.WriteAllBytesAsync(path, original);
        store.TryAdd(new FileTransferSession
        {
            ConversationId = "peer-1",
            TransferId = transferId,
            FileName = "shared.bin",
            SizeBytes = 4,
            IsIncoming = true,
            State = FileTransferState.Accepted,
            SavePath = path
        });

        var pipe = new Pipe();
        using var cancellation = new CancellationTokenSource();
        try
        {
            var handler = new FileTransferPayloadHandler(sink, store);
            var receive = handler.HandleAsync(new FileChatMessage("peer-1", transferId, "shared.bin", 4),
                pipe.Reader, cancellation.Token).AsTask();
            Assert.AreEqual(1, Directory.GetFiles(Path.GetTempPath(),
                $".{Path.GetFileName(path)}.*.tmp").Length);

            cancellation.Cancel();
            try
            {
                await receive.WaitAsync(TimeSpan.FromSeconds(1));
                Assert.Fail("Cancelled receive should not complete successfully.");
            }
            catch (OperationCanceledException)
            {
            }

            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(path));
            Assert.AreEqual(0, Directory.GetFiles(Path.GetTempPath(),
                $".{Path.GetFileName(path)}.*.tmp").Length);
        }
        finally
        {
            await pipe.Writer.CompleteAsync();
            await pipe.Reader.CompleteAsync();
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task HandleAsync_ConcurrentPayload_DoesNotOpenTargetTwice()
    {
        var sink = new RecordingSink();
        var store = new FileTransferSessionStore();
        var transferId = Guid.NewGuid();
        var opened = 0;
        store.TryAdd(new FileTransferSession
        {
            ConversationId = "peer-1",
            TransferId = transferId,
            FileName = "shared.bin",
            SizeBytes = 4,
            IsIncoming = true,
            State = FileTransferState.Accepted,
            OpenWriteStreamAsync = _ =>
            {
                opened++;
                return new ValueTask<Stream>(new MemoryStream());
            }
        });

        var handler = new FileTransferPayloadHandler(sink, store);
        var pending = new Pipe();
        var first = handler.HandleAsync(
            new FileChatMessage("peer-1", transferId, "shared.bin", 4), pending.Reader, CancellationToken.None)
            .AsTask();

        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await handler.HandleAsync(
            new FileChatMessage("peer-1", transferId, "shared.bin", 4),
            PipeReader.Create(new MemoryStream(new byte[] { 1, 2, 3, 4 })),
            CancellationToken.None));

        await pending.Writer.WriteAsync(new byte[] { 1, 2, 3, 4 });
        await pending.Writer.CompleteAsync();
        await first;
        Assert.AreEqual(1, opened);
    }

    private sealed class RecordingSink : IIncomingMessageSink
    {
        public List<DeviceMessageEvent> Events { get; } = [];

        public ValueTask PublishAsync(
            Kitopia.Feature.DeviceCommunication.Messages.AppMessage message,
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
