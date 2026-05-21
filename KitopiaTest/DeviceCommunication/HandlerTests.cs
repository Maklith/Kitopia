using System.IO.Pipelines;
using System.Net;
using Core.Services.DeviceCommunication;
using Core.Services.DeviceCommunication.Application;
using Core.Services.DeviceCommunication.Codecs;
using Core.Services.DeviceCommunication.Handlers;
using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Messages.Clipboard;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Routing;
using Core.Services.DeviceCommunication.Sessions;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class HandlerTests
{
    #region DeviceMessageDispatcher - Chat Text Messages

    [TestMethod]
    public async Task DeviceMessageDispatcher_ChatTextMessage_PublishesToSink()
    {
        var sink = new RecordingSink();
        var dispatcher = CreateDispatcher(sink: sink);
        var envelope = new DataEnvelope
        {
            Route = "chat", Command = "text", StreamType = DataStreamType.Text,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "peer-1", ["text"] = "hello"
            }
        };

        await dispatcher.DispatchAsync(CreateContext(), envelope, PipeReader.Create(Stream.Null));

        Assert.AreEqual(1, sink.Events.Count);
        Assert.IsInstanceOfType<TextChatMessage>(sink.Events[0].Message);
    }

    [TestMethod]
    public async Task DeviceMessageDispatcher_ChatControlMessage_PublishesToSink()
    {
        var sink = new RecordingSink();
        var dispatcher = CreateDispatcher(sink: sink);
        var envelope = new DataEnvelope
        {
            Route = "chat", Command = "file.accept", StreamType = DataStreamType.Control,
            ChannelId = Guid.NewGuid(),
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal) { ["conversationId"] = "peer-1" }
        };

        await dispatcher.DispatchAsync(CreateContext(), envelope, PipeReader.Create(Stream.Null));

        Assert.AreEqual(1, sink.Events.Count);
        Assert.IsInstanceOfType<FileAcceptChatMessage>(sink.Events[0].Message);
    }

    #endregion

    #region DeviceMessageDispatcher - Chat Image Messages

    [TestMethod]
    public async Task DeviceMessageDispatcher_ChatImageMessage_PublishesPayloadBytesToSink()
    {
        var sink = new RecordingSink();
        var dispatcher = CreateDispatcher(sink: sink);
        var transferId = Guid.NewGuid();
        var envelope = new DataEnvelope
        {
            Route = "chat", Command = "image.direct", StreamType = DataStreamType.Image,
            ChannelId = transferId,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "peer-1", ["sizeBytes"] = "4", ["senderId"] = "peer-1"
            }
        };
        var payloadBytes = new byte[] { 10, 20, 30, 40 };
        var reader = PipeReader.Create(new MemoryStream(payloadBytes));

        await dispatcher.DispatchAsync(CreateContext(), envelope, reader);

        Assert.AreEqual(1, sink.Events.Count);
        Assert.IsNotNull(sink.Events[0].PayloadBytes);
        CollectionAssert.AreEqual(payloadBytes, sink.Events[0].PayloadBytes);
    }

    #endregion

    #region DeviceMessageDispatcher - Chat File Messages

    [TestMethod]
    public async Task DeviceMessageDispatcher_ChatFileMessage_WithAcceptedSession_SavesToFile()
    {
        var sink = new RecordingSink();
        var sessionStore = new FileTransferSessionStore();
        var dispatcher = CreateDispatcher(sink: sink, sessionStore: sessionStore);

        var transferId = Guid.NewGuid();
        var tempFile = Path.Combine(Path.GetTempPath(), $"kitopia-test-{transferId:D}.bin");
        var payloadBytes = new byte[] { 1, 2, 3, 4 };

        sessionStore.TryAdd(new FileTransferSession
        {
            ConversationId = "peer-1",
            TransferId = transferId,
            FileName = "test.bin",
            SizeBytes = payloadBytes.Length,
            State = FileTransferState.Accepted,
            SavePath = tempFile
        });

        var envelope = new DataEnvelope
        {
            Route = "chat", Command = "file", StreamType = DataStreamType.File,
            ChannelId = transferId,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "peer-1", ["senderId"] = "peer-1",
                ["fileName"] = "test.bin", ["length"] = payloadBytes.Length.ToString()
            }
        };

        try
        {
            await dispatcher.DispatchAsync(CreateContext(), envelope, PipeReader.Create(new MemoryStream(payloadBytes)));

            Assert.IsTrue(File.Exists(tempFile));
            var saved = await File.ReadAllBytesAsync(tempFile);
            CollectionAssert.AreEqual(payloadBytes, saved);

            var completedEvent = sink.Events.FirstOrDefault(e => e.EventType == IncomingMessageEventType.TransferCompleted);
            Assert.IsNotNull(completedEvent);
            Assert.IsInstanceOfType<FileCompleteChatMessage>(completedEvent.Message);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task DeviceMessageDispatcher_ChatFileMessage_WithoutAcceptedSession_DrainsAndRejects()
    {
        var sink = new RecordingSink();
        var dispatcher = CreateDispatcher(sink: sink);
        var transferId = Guid.NewGuid();
        var envelope = new DataEnvelope
        {
            Route = "chat", Command = "file", StreamType = DataStreamType.File,
            ChannelId = transferId,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "peer-1", ["senderId"] = "peer-1",
                ["fileName"] = "test.bin", ["length"] = "4"
            }
        };
        var payloadBytes = new byte[] { 1, 2, 3, 4 };

        await dispatcher.DispatchAsync(CreateContext(), envelope, PipeReader.Create(new MemoryStream(payloadBytes)));

        var rejectEvent = sink.Events.FirstOrDefault(e => e.EventType == IncomingMessageEventType.TransferRejected);
        Assert.IsNotNull(rejectEvent);
        Assert.IsInstanceOfType<FileRejectChatMessage>(rejectEvent.Message);
        Assert.AreEqual("missing_accept_session", rejectEvent.Reason);
    }

    [TestMethod]
    public async Task DeviceMessageDispatcher_ChatFileMessage_WithOfferedState_DrainsAndRejects()
    {
        var sink = new RecordingSink();
        var sessionStore = new FileTransferSessionStore();
        var dispatcher = CreateDispatcher(sink: sink, sessionStore: sessionStore);

        var transferId = Guid.NewGuid();
        sessionStore.TryAdd(new FileTransferSession
        {
            ConversationId = "peer-1",
            TransferId = transferId,
            FileName = "test.bin",
            SizeBytes = 4,
            State = FileTransferState.Offered,
            SavePath = "/tmp/test.bin"
        });

        var envelope = new DataEnvelope
        {
            Route = "chat", Command = "file", StreamType = DataStreamType.File,
            ChannelId = transferId,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "peer-1", ["senderId"] = "peer-1",
                ["fileName"] = "test.bin", ["length"] = "4"
            }
        };

        await dispatcher.DispatchAsync(CreateContext(), envelope, PipeReader.Create(new MemoryStream(new byte[] { 1, 2, 3, 4 })));

        var rejectEvent = sink.Events.FirstOrDefault(e => e.EventType == IncomingMessageEventType.TransferRejected);
        Assert.IsNotNull(rejectEvent);
    }

    [TestMethod]
    public async Task DeviceMessageDispatcher_ChatFileMessage_WhenReceiveWriteFails_RemovesSessionAndPublishesRejected()
    {
        var sink = new RecordingSink();
        var sessionStore = new FileTransferSessionStore();
        var dispatcher = CreateDispatcher(sink: sink, sessionStore: sessionStore);

        var transferId = Guid.NewGuid();
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"kitopia-test-{transferId:D}");
        Directory.CreateDirectory(tempDirectory);

        sessionStore.TryAdd(new FileTransferSession
        {
            ConversationId = "peer-1",
            TransferId = transferId,
            FileName = "test.bin",
            SizeBytes = 4,
            State = FileTransferState.Accepted,
            SavePath = tempDirectory
        });

        var envelope = new DataEnvelope
        {
            Route = "chat", Command = "file", StreamType = DataStreamType.File,
            ChannelId = transferId,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "peer-1", ["senderId"] = "peer-1",
                ["fileName"] = "test.bin", ["length"] = "4"
            }
        };

        try
        {
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
                dispatcher.DispatchAsync(CreateContext(), envelope, PipeReader.Create(new MemoryStream(new byte[] { 1, 2, 3, 4 }))).AsTask());

            Assert.IsFalse(sessionStore.TryGet(transferId, out _));
            var rejectEvent = sink.Events.FirstOrDefault(e => e.EventType == IncomingMessageEventType.TransferRejected);
            Assert.IsNotNull(rejectEvent);
            Assert.IsInstanceOfType<FileRejectChatMessage>(rejectEvent.Message);
            Assert.AreEqual("receive_failed", rejectEvent.Reason);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task DeviceMessageDispatcher_ChatFileMessage_WhenReceiveCancelled_RemovesSessionAndPublishesCancelled()
    {
        var sink = new RecordingSink();
        var sessionStore = new FileTransferSessionStore();
        var dispatcher = CreateDispatcher(sink: sink, sessionStore: sessionStore);

        var transferId = Guid.NewGuid();
        var tempFile = Path.Combine(Path.GetTempPath(), $"kitopia-test-{transferId:D}.bin");

        sessionStore.TryAdd(new FileTransferSession
        {
            ConversationId = "peer-1",
            TransferId = transferId,
            FileName = "test.bin",
            SizeBytes = 4,
            State = FileTransferState.Accepted,
            SavePath = tempFile
        });

        var envelope = new DataEnvelope
        {
            Route = "chat", Command = "file", StreamType = DataStreamType.File,
            ChannelId = transferId,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "peer-1", ["senderId"] = "peer-1",
                ["fileName"] = "test.bin", ["length"] = "4"
            }
        };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        try
        {
            try
            {
                await dispatcher.DispatchAsync(
                    CreateContext(),
                    envelope,
                    PipeReader.Create(new MemoryStream(new byte[] { 1, 2, 3, 4 })),
                    cts.Token);
                Assert.Fail("Expected receive cancellation to throw.");
            }
            catch (OperationCanceledException)
            {
            }

            Assert.IsFalse(sessionStore.TryGet(transferId, out _));
            var cancelEvent = sink.Events.FirstOrDefault(e => e.EventType == IncomingMessageEventType.TransferCancelled);
            Assert.IsNotNull(cancelEvent);
            Assert.IsInstanceOfType<FileCancelChatMessage>(cancelEvent.Message);
            Assert.AreEqual("cancelled", cancelEvent.Reason);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    #endregion

    #region DeviceMessageDispatcher - Edge Cases

    [TestMethod]
    public async Task DeviceMessageDispatcher_UnknownRouteCommand_DoesNothing()
    {
        var sink = new RecordingSink();
        var dispatcher = CreateDispatcher(sink: sink);
        var envelope = new DataEnvelope
        {
            Route = "chat", Command = "unknown", StreamType = DataStreamType.Text,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "peer-1"
            }
        };

        await dispatcher.DispatchAsync(CreateContext(), envelope, PipeReader.Create(Stream.Null));

        Assert.AreEqual(0, sink.Events.Count);
    }

    [TestMethod]
    public async Task DeviceMessageDispatcher_EmptyMetadata_DoesNothing()
    {
        var sink = new RecordingSink();
        var dispatcher = CreateDispatcher(sink: sink);
        var envelope = new DataEnvelope { Route = "chat", Command = "text", StreamType = DataStreamType.Text };

        await dispatcher.DispatchAsync(CreateContext(), envelope, PipeReader.Create(Stream.Null));

        Assert.AreEqual(0, sink.Events.Count);
    }

    #endregion

    #region DeviceMessageDispatcher - Clipboard

    [TestMethod]
    public async Task DeviceMessageDispatcher_ClipboardTextMessage_PublishesToSink()
    {
        var sink = new RecordingSink();
        var dispatcher = CreateDispatcher(sink: sink);
        var envelope = new DataEnvelope
        {
            Route = "clipboard", Command = "text", StreamType = DataStreamType.Text,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "peer-1", ["text"] = "clipboard content"
            }
        };

        await dispatcher.DispatchAsync(CreateContext(), envelope, PipeReader.Create(Stream.Null));

        Assert.AreEqual(1, sink.Events.Count);
        Assert.IsInstanceOfType<TextClipboardMessage>(sink.Events[0].Message);
    }

    [TestMethod]
    public async Task DeviceMessageDispatcher_ClipboardUnknownCommand_DoesNothing()
    {
        var sink = new RecordingSink();
        var dispatcher = CreateDispatcher(sink: sink);
        var envelope = new DataEnvelope
        {
            Route = "clipboard", Command = "unknown", StreamType = DataStreamType.Text,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal) { ["conversationId"] = "peer-1" }
        };

        await dispatcher.DispatchAsync(CreateContext(), envelope, PipeReader.Create(Stream.Null));

        Assert.AreEqual(0, sink.Events.Count);
    }

    [TestMethod]
    public async Task DeviceMessageDispatcher_ClipboardMessage_PublishesAfterSuccessfulDecode()
    {
        var sink = new RecordingSink();
        var dispatcher = CreateDispatcher(sink: sink);
        var envelope = new DataEnvelope
        {
            Route = "clipboard", Command = "text", StreamType = (DataStreamType)99,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "peer-1", ["text"] = "hi"
            }
        };

        await dispatcher.DispatchAsync(CreateContext(), envelope, PipeReader.Create(Stream.Null));

        Assert.AreEqual(1, sink.Events.Count);
        Assert.IsInstanceOfType<TextClipboardMessage>(sink.Events[0].Message);
    }

    #endregion

    #region ImageTransferPolicy

    [TestMethod]
    public void ImageTransferPolicy_ShouldDirectSend_True_ForSmallImage()
    {
        var policy = new ImageTransferPolicy();
        Assert.IsTrue(policy.ShouldDirectSend(1024));
    }

    [TestMethod]
    public void ImageTransferPolicy_ShouldDirectSend_True_AtThreshold()
    {
        var policy = new ImageTransferPolicy();
        Assert.IsTrue(policy.ShouldDirectSend(ImageTransferPolicy.DirectSendThresholdBytes));
    }

    [TestMethod]
    public void ImageTransferPolicy_ShouldDirectSend_False_ForLargeImage()
    {
        var policy = new ImageTransferPolicy();
        Assert.IsFalse(policy.ShouldDirectSend(ImageTransferPolicy.DirectSendThresholdBytes + 1));
    }

    [TestMethod]
    public void ImageTransferPolicy_ShouldDirectSend_False_ForZeroSize()
    {
        var policy = new ImageTransferPolicy();
        Assert.IsFalse(policy.ShouldDirectSend(0));
    }

    [TestMethod]
    public void ImageTransferPolicy_ShouldDirectSend_False_ForNegativeSize()
    {
        var policy = new ImageTransferPolicy();
        Assert.IsFalse(policy.ShouldDirectSend(-1));
    }

    #endregion

    #region Helpers

    private static MessageContext CreateContext()
    {
        return new MessageContext(
            LocalDataTransportProtocol.Tcp,
            new IPEndPoint(IPAddress.Loopback, 12345),
            "peer-1");
    }

    private static DeviceMessageDispatcher CreateDispatcher(
        RecordingSink? sink = null,
        FileTransferSessionStore? sessionStore = null)
    {
        sink ??= new RecordingSink();
        sessionStore ??= new FileTransferSessionStore();
        var registry = new MessageCodecRegistry();
        return new DeviceMessageDispatcher(registry, sink, new FileTransferPayloadHandler(sink, sessionStore));
    }

    private sealed class RecordingSink : IIncomingMessageSink
    {
        public List<IncomingMessageEvent> Events { get; } = [];

        public ValueTask PublishAsync(Core.Services.DeviceCommunication.Messages.AppMessage message,
            CancellationToken cancellationToken = default)
        {
            Events.Add(new IncomingMessageEvent(message));
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishEventAsync(IncomingMessageEvent messageEvent,
            CancellationToken cancellationToken = default)
        {
            Events.Add(messageEvent);
            return ValueTask.CompletedTask;
        }
    }

    #endregion
}
