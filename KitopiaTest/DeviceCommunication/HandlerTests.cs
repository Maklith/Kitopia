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
    #region ChatRouteHandler - Text Messages

    [TestMethod]
    public async Task ChatRouteHandler_TextMessage_PublishesToSink()
    {
        var sink = new RecordingSink();
        var handler = CreateChatHandler(sink: sink);
        var envelope = new DataEnvelope
        {
            Route = "chat", Command = "text", StreamType = DataStreamType.Text,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "peer-1", ["text"] = "hello"
            }
        };

        await handler.HandleAsync(CreateContext(), envelope, PipeReader.Create(Stream.Null));

        Assert.AreEqual(1, sink.Events.Count);
        Assert.IsInstanceOfType<TextChatMessage>(sink.Events[0].Message);
    }

    [TestMethod]
    public async Task ChatRouteHandler_ControlMessage_PublishesToSink()
    {
        var sink = new RecordingSink();
        var handler = CreateChatHandler(sink: sink);
        var envelope = new DataEnvelope
        {
            Route = "chat", Command = "file.accept", StreamType = DataStreamType.Control,
            ChannelId = Guid.NewGuid(),
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal) { ["conversationId"] = "peer-1" }
        };

        await handler.HandleAsync(CreateContext(), envelope, PipeReader.Create(Stream.Null));

        Assert.AreEqual(1, sink.Events.Count);
        Assert.IsInstanceOfType<FileAcceptChatMessage>(sink.Events[0].Message);
    }

    #endregion

    #region ChatRouteHandler - Image Messages

    [TestMethod]
    public async Task ChatRouteHandler_ImageMessage_PublishesPayloadBytesToSink()
    {
        var sink = new RecordingSink();
        var handler = CreateChatHandler(sink: sink);
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

        await handler.HandleAsync(CreateContext(), envelope, reader);

        Assert.AreEqual(1, sink.Events.Count);
        Assert.IsNotNull(sink.Events[0].PayloadBytes);
        CollectionAssert.AreEqual(payloadBytes, sink.Events[0].PayloadBytes);
    }

    #endregion

    #region ChatRouteHandler - File Messages

    [TestMethod]
    public async Task ChatRouteHandler_FileMessage_WithAcceptedSession_SavesToFile()
    {
        var sink = new RecordingSink();
        var sessionStore = new FileTransferSessionStore();
        var handler = CreateChatHandler(sink: sink, sessionStore: sessionStore);

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
            await handler.HandleAsync(CreateContext(), envelope, PipeReader.Create(new MemoryStream(payloadBytes)));

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
    public async Task ChatRouteHandler_FileMessage_WithoutAcceptedSession_DrainsAndRejects()
    {
        var sink = new RecordingSink();
        var handler = CreateChatHandler(sink: sink);
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

        await handler.HandleAsync(CreateContext(), envelope, PipeReader.Create(new MemoryStream(payloadBytes)));

        var rejectEvent = sink.Events.FirstOrDefault(e => e.EventType == IncomingMessageEventType.TransferRejected);
        Assert.IsNotNull(rejectEvent);
        Assert.IsInstanceOfType<FileRejectChatMessage>(rejectEvent.Message);
        Assert.AreEqual("missing_accept_session", rejectEvent.Reason);
    }

    [TestMethod]
    public async Task ChatRouteHandler_FileMessage_WithOfferedState_DrainsAndRejects()
    {
        var sink = new RecordingSink();
        var sessionStore = new FileTransferSessionStore();
        var handler = CreateChatHandler(sink: sink, sessionStore: sessionStore);

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

        await handler.HandleAsync(CreateContext(), envelope, PipeReader.Create(new MemoryStream(new byte[] { 1, 2, 3, 4 })));

        var rejectEvent = sink.Events.FirstOrDefault(e => e.EventType == IncomingMessageEventType.TransferRejected);
        Assert.IsNotNull(rejectEvent);
    }

    #endregion

    #region ChatRouteHandler - Edge Cases

    [TestMethod]
    public async Task ChatRouteHandler_UnknownRouteCommand_DoesNothing()
    {
        var sink = new RecordingSink();
        var handler = CreateChatHandler(sink: sink);
        var envelope = new DataEnvelope
        {
            Route = "chat", Command = "unknown", StreamType = DataStreamType.Text,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "peer-1"
            }
        };

        await handler.HandleAsync(CreateContext(), envelope, PipeReader.Create(Stream.Null));

        Assert.AreEqual(0, sink.Events.Count);
    }

    [TestMethod]
    public async Task ChatRouteHandler_EmptyMetadata_DoesNothing()
    {
        var sink = new RecordingSink();
        var handler = CreateChatHandler(sink: sink);
        var envelope = new DataEnvelope { Route = "chat", Command = "text", StreamType = DataStreamType.Text };

        await handler.HandleAsync(CreateContext(), envelope, PipeReader.Create(Stream.Null));

        Assert.AreEqual(0, sink.Events.Count);
    }

    #endregion

    #region ClipboardRouteHandler

    [TestMethod]
    public async Task ClipboardRouteHandler_TextMessage_PublishesToSink()
    {
        var sink = new RecordingSink();
        var handler = CreateClipboardHandler(sink: sink);
        var envelope = new DataEnvelope
        {
            Route = "clipboard", Command = "text", StreamType = DataStreamType.Text,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "peer-1", ["text"] = "clipboard content"
            }
        };

        await handler.HandleAsync(CreateContext(), envelope, PipeReader.Create(Stream.Null));

        Assert.AreEqual(1, sink.Events.Count);
        Assert.IsInstanceOfType<TextClipboardMessage>(sink.Events[0].Message);
    }

    [TestMethod]
    public async Task ClipboardRouteHandler_UnknownCommand_DoesNothing()
    {
        var sink = new RecordingSink();
        var handler = CreateClipboardHandler(sink: sink);
        var envelope = new DataEnvelope
        {
            Route = "clipboard", Command = "unknown", StreamType = DataStreamType.Text,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal) { ["conversationId"] = "peer-1" }
        };

        await handler.HandleAsync(CreateContext(), envelope, PipeReader.Create(Stream.Null));

        Assert.AreEqual(0, sink.Events.Count);
    }

    [TestMethod]
    public async Task ClipboardRouteHandler_UnsupportedStreamType_Throws()
    {
        var sink = new RecordingSink();
        var handler = CreateClipboardHandler(sink: sink);
        var envelope = new DataEnvelope
        {
            Route = "clipboard", Command = "text", StreamType = (DataStreamType)99,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "peer-1", ["text"] = "hi"
            }
        };

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => handler.HandleAsync(CreateContext(), envelope, PipeReader.Create(Stream.Null)).AsTask());
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

    private static ChatRouteHandler CreateChatHandler(
        RecordingSink? sink = null,
        FileTransferSessionStore? sessionStore = null)
    {
        sink ??= new RecordingSink();
        sessionStore ??= new FileTransferSessionStore();
        var registry = new MessageCodecRegistry(new IMessageCodec[]
        {
            new ChatMessageCodec(),
            new FileChatMessageCodec(),
            new ImageChatMessageCodec(),
            new FileOfferChatMessageCodec(),
            new FileAcceptChatMessageCodec(),
            new FileRejectChatMessageCodec(),
            new FileCancelChatMessageCodec(),
            new FileCompleteChatMessageCodec()
        });
        return new ChatRouteHandler(registry, sink, sessionStore);
    }

    private static ClipboardRouteHandler CreateClipboardHandler(RecordingSink? sink = null)
    {
        sink ??= new RecordingSink();
        var registry = new MessageCodecRegistry(new IMessageCodec[] { new ClipboardMessageCodec() });
        return new ClipboardRouteHandler(registry, sink);
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
