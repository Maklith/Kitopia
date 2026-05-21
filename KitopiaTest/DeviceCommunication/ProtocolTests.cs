using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Text.Json;
using Core.Services.DeviceCommunication;
using Core.Services.DeviceCommunication.Application;
using Core.Services.DeviceCommunication.Codecs;
using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Routing;
using Core.Services.DeviceCommunication.Sessions;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class ProtocolTests
{
    #region ProtocolSender

    [TestMethod]
    public async Task SendAsync_SendsFrameWithMagicAndEnvelope()
    {
        var listener = new FakeLocalDataListener();
        var sender = new ProtocolSender(listener);
        var context = CreateContext();
        var envelope = new DataEnvelope { Route = "chat", Command = "text", StreamType = DataStreamType.Text };

        await sender.SendAsync(context, envelope);

        Assert.AreEqual(1, listener.SendCallCount);
        Assert.IsNotNull(listener.LastPipeData);

        var payload = new MemoryStream(listener.LastPipeData!);
        var frameHeader = new byte[16];
        await payload.ReadExactlyAsync(frameHeader);
        Assert.AreEqual("KDC1", Encoding.ASCII.GetString(frameHeader, 0, 4));

        var envelopeLength = BinaryPrimitives.ReadInt32LittleEndian(frameHeader.AsSpan(4, 4));
        Assert.IsTrue(envelopeLength > 0);

        var envelopeBytes = new byte[envelopeLength];
        await payload.ReadExactlyAsync(envelopeBytes);
        var decoded = JsonSerializer.Deserialize<DataEnvelope>(envelopeBytes);
        Assert.IsNotNull(decoded);
        Assert.AreEqual("chat", decoded.Route);
        Assert.AreEqual("text", decoded.Command);
    }

    [TestMethod]
    public async Task SendAsync_WithPayload_SendsFrameEnvelopeAndPayload()
    {
        var listener = new FakeLocalDataListener();
        var sender = new ProtocolSender(listener);
        var context = CreateContext();
        var envelope = new DataEnvelope { Route = "chat", Command = "file", StreamType = DataStreamType.File, ChannelId = Guid.NewGuid() };
        var payloadBytes = new byte[] { 1, 2, 3, 4, 5 };
        using var payloadStream = new MemoryStream(payloadBytes, writable: false);

        await sender.SendAsync(context, envelope, payloadStream);

        Assert.AreEqual(1, listener.SendCallCount);
        Assert.IsNotNull(listener.LastPipeData);

        var fullData = listener.LastPipeData!;
        var payload = new MemoryStream(fullData);
        var frameHeader = new byte[16];
        await payload.ReadExactlyAsync(frameHeader);

        var envelopeLength = BinaryPrimitives.ReadInt32LittleEndian(frameHeader.AsSpan(4, 4));
        var filePayloadLength = BinaryPrimitives.ReadInt64LittleEndian(frameHeader.AsSpan(8, 8));
        Assert.AreEqual(payloadBytes.Length, filePayloadLength);

        var envelopeBytes = new byte[envelopeLength];
        await payload.ReadExactlyAsync(envelopeBytes);
        var decoded = JsonSerializer.Deserialize<DataEnvelope>(envelopeBytes);
        Assert.AreEqual("chat", decoded!.Route);

        var actualPayload = new byte[filePayloadLength];
        await payload.ReadExactlyAsync(actualPayload);
        CollectionAssert.AreEqual(payloadBytes, actualPayload);
    }

    [TestMethod]
    public async Task SendAsync_WithPayload_ReportsProgress()
    {
        var listener = new FakeLocalDataListener();
        var sender = new ProtocolSender(listener);
        var context = CreateContext();
        var envelope = new DataEnvelope { Route = "chat", Command = "file", StreamType = DataStreamType.File };
        var payloadBytes = new byte[2 * 1024 * 1024];
        Random.Shared.NextBytes(payloadBytes);
        using var payloadStream = new MemoryStream(payloadBytes, writable: false);

        var progressReports = new List<(long Sent, long Total)>();
        await sender.SendAsync(context, envelope, payloadStream,
            (sent, total) =>
            {
                progressReports.Add((sent, total));
                return ValueTask.CompletedTask;
            });

        Assert.IsTrue(progressReports.Count > 0);
        var last = progressReports[^1];
        Assert.AreEqual(payloadBytes.Length, last.Sent);
        Assert.AreEqual(payloadBytes.Length, last.Total);
    }

    [TestMethod]
    public async Task SendAsync_WithPayload_Throws_WhenStreamNotReadable()
    {
        var listener = new FakeLocalDataListener();
        var sender = new ProtocolSender(listener);
        var envelope = new DataEnvelope { Route = "chat", Command = "file", StreamType = DataStreamType.File };
        var unreadable = new NonReadableStream();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => sender.SendAsync(CreateContext(), envelope, unreadable));
    }

    #endregion

    #region ProtocolSession

    [TestMethod]
    public async Task HandleAsync_DoesNothing_WhenEmptyPayload()
    {
        var sink = new RecordingSink();
        var session = CreateProtocolSession(sink);

        var reader = PipeReader.Create(Stream.Null);
        await session.HandleAsync(LocalDataTransportProtocol.Tcp, new IPEndPoint(IPAddress.Loopback, 12345), reader);

        Assert.AreEqual(0, sink.Events.Count);
    }

    [TestMethod]
    public async Task HandleAsync_Throws_WhenInvalidMagic()
    {
        var session = CreateProtocolSession();

        var badFrame = new byte[16];
        Encoding.ASCII.GetBytes("XXXX").CopyTo(badFrame, 0);
        BinaryPrimitives.WriteInt32LittleEndian(badFrame.AsSpan(4, 4), 10);
        BinaryPrimitives.WriteInt64LittleEndian(badFrame.AsSpan(8, 8), 0);

        var reader = PipeReader.Create(new MemoryStream(badFrame));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => session.HandleAsync(LocalDataTransportProtocol.Tcp, new IPEndPoint(IPAddress.Loopback, 12345), reader).AsTask());
    }

    [TestMethod]
    public async Task HandleAsync_Throws_WhenEnvelopeLengthZero()
    {
        var session = CreateProtocolSession();

        var frame = new byte[16];
        Encoding.ASCII.GetBytes("KDC1").CopyTo(frame, 0);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), 0);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), 0);

        var reader = PipeReader.Create(new MemoryStream(frame));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => session.HandleAsync(LocalDataTransportProtocol.Tcp, new IPEndPoint(IPAddress.Loopback, 12345), reader).AsTask());
    }

    [TestMethod]
    public async Task HandleAsync_Throws_WhenPayloadLengthNegative()
    {
        var session = CreateProtocolSession();

        var frame = new byte[16];
        Encoding.ASCII.GetBytes("KDC1").CopyTo(frame, 0);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), 10);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), -1);

        var reader = PipeReader.Create(new MemoryStream(frame));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => session.HandleAsync(LocalDataTransportProtocol.Tcp, new IPEndPoint(IPAddress.Loopback, 12345), reader).AsTask());
    }

    [TestMethod]
    public async Task HandleAsync_SkipsRouting_WhenEnvelopeRouteBlank()
    {
        var sink = new RecordingSink();
        var session = CreateProtocolSession(sink);

        var envelope = new DataEnvelope { Route = "", Command = "text", StreamType = DataStreamType.Text };
        var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var frame = BuildTestFrame(envelopeBytes, 0);
        var fullData = frame.Concat(envelopeBytes).ToArray();

        var reader = PipeReader.Create(new MemoryStream(fullData));
        await session.HandleAsync(LocalDataTransportProtocol.Tcp, new IPEndPoint(IPAddress.Loopback, 12345), reader);

        Assert.AreEqual(0, sink.Events.Count);
    }

    [TestMethod]
    public async Task HandleAsync_RoutesEnvelope_WithNoPayload()
    {
        var sink = new RecordingSink();
        var session = CreateProtocolSession(sink);

        var envelope = new DataEnvelope
        {
            Route = "chat",
            Command = "text",
            StreamType = DataStreamType.Text,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "peer-1",
                ["text"] = "hello"
            }
        };
        var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var frame = BuildTestFrame(envelopeBytes, 0);
        var fullData = frame.Concat(envelopeBytes).ToArray();

        var reader = PipeReader.Create(new MemoryStream(fullData));
        await session.HandleAsync(LocalDataTransportProtocol.Tcp, new IPEndPoint(IPAddress.Loopback, 12345), reader);

        Assert.AreEqual(1, sink.Events.Count);
        Assert.IsInstanceOfType<TextChatMessage>(sink.Events[0].Message);
    }

    [TestMethod]
    public async Task HandleAsync_RoutesEnvelope_WithPayload()
    {
        var sink = new RecordingSink();
        var session = CreateProtocolSession(sink);

        var envelope = new DataEnvelope
        {
            Route = "chat",
            Command = "image.direct",
            StreamType = DataStreamType.Image,
            ChannelId = Guid.NewGuid(),
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "peer-1",
                ["sizeBytes"] = "3"
            }
        };
        var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var payloadData = new byte[] { 10, 20, 30 };
        var frame = BuildTestFrame(envelopeBytes, payloadData.Length);
        var fullData = frame.Concat(envelopeBytes).Concat(payloadData).ToArray();

        var reader = PipeReader.Create(new MemoryStream(fullData));
        await session.HandleAsync(LocalDataTransportProtocol.Tcp, new IPEndPoint(IPAddress.Loopback, 12345), reader);

        Assert.AreEqual(1, sink.Events.Count);
        Assert.IsNotNull(sink.Events[0].PayloadBytes);
        CollectionAssert.AreEqual(payloadData, sink.Events[0].PayloadBytes);
    }

    [TestMethod]
    public async Task HandleAsync_DoesNotExposeBytesBeyondDeclaredPayloadLength()
    {
        var sink = new RecordingSink();
        var session = CreateProtocolSession(sink);

        var envelope = new DataEnvelope
        {
            Route = "chat",
            Command = "image.direct",
            StreamType = DataStreamType.Image,
            ChannelId = Guid.NewGuid(),
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "peer-1",
                ["sizeBytes"] = "3"
            }
        };
        var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var declaredPayload = new byte[] { 10, 20, 30 };
        var trailingBytes = new byte[] { 40, 50, 60 };
        var frame = BuildTestFrame(envelopeBytes, declaredPayload.Length);
        var fullData = frame.Concat(envelopeBytes).Concat(declaredPayload).Concat(trailingBytes).ToArray();

        var reader = PipeReader.Create(new MemoryStream(fullData));
        await session.HandleAsync(LocalDataTransportProtocol.Tcp, new IPEndPoint(IPAddress.Loopback, 12345), reader);

        Assert.AreEqual(1, sink.Events.Count);
        CollectionAssert.AreEqual(declaredPayload, sink.Events[0].PayloadBytes);
    }

    [TestMethod]
    public async Task HandleAsync_Throws_WhenEnvelopeTruncated()
    {
        var session = CreateProtocolSession();
        var frame = new byte[16];
        Encoding.ASCII.GetBytes("KDC1").CopyTo(frame, 0);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), 10);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), 0);
        var fullData = frame.Concat(new byte[] { 1, 2, 3 }).ToArray();

        var reader = PipeReader.Create(new MemoryStream(fullData));

        await Assert.ThrowsExactlyAsync<EndOfStreamException>(
            () => session.HandleAsync(LocalDataTransportProtocol.Tcp, new IPEndPoint(IPAddress.Loopback, 12345), reader).AsTask());
    }

    [TestMethod]
    public async Task HandleAsync_FillsSenderIp_WhenMetadataSenderMissing()
    {
        var sink = new RecordingSink();
        var session = CreateProtocolSession(sink);
        var remoteIp = IPAddress.Parse("192.168.1.100");

        var envelope = new DataEnvelope
        {
            Route = "chat",
            Command = "text",
            StreamType = DataStreamType.Text,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "peer-1",
                ["text"] = "hello"
            }
        };
        var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var frame = BuildTestFrame(envelopeBytes, 0);
        var fullData = frame.Concat(envelopeBytes).ToArray();

        var reader = PipeReader.Create(new MemoryStream(fullData));
        await session.HandleAsync(LocalDataTransportProtocol.Tcp, new IPEndPoint(remoteIp, 9999), reader);

        Assert.AreEqual(1, sink.Events.Count);
        Assert.AreEqual("192.168.1.100", sink.Events[0].Message.ConversationId);
    }

    #endregion

    #region Helpers

    private static MessageContext CreateContext()
    {
        return new MessageContext(
            LocalDataTransportProtocol.Tcp,
            new IPEndPoint(IPAddress.Loopback, 12345),
            "peer-key");
    }

    private static byte[] BuildTestFrame(byte[] envelopeBytes, int payloadLength)
    {
        var frame = new byte[16];
        Encoding.ASCII.GetBytes("KDC1").CopyTo(frame, 0);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), envelopeBytes.Length);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), payloadLength);
        return frame;
    }

    private static ProtocolSession CreateProtocolSession(RecordingSink? sink = null)
    {
        sink ??= new RecordingSink();
        var sessionStore = new FileTransferSessionStore();
        return new ProtocolSession(new DeviceMessageDispatcher(
            new MessageCodecRegistry(),
            sink,
            new FileTransferPayloadHandler(sink, sessionStore)));
    }

    private static async Task ReadFromPipeReaderAsync(PipeReader reader, byte[] destination)
    {
        var filled = 0;
        while (filled < destination.Length)
        {
            var result = await reader.ReadAsync();
            var buffer = result.Buffer;
            if (buffer.IsEmpty && result.IsCompleted) break;
            var toCopy = (int)Math.Min(buffer.Length, destination.Length - filled);
            var sliced = buffer.Slice(0, toCopy);
            var offset = filled;
            foreach (var segment in sliced)
            {
                segment.Span.CopyTo(destination.AsSpan(offset));
                offset += segment.Length;
            }
            filled += toCopy;
            reader.AdvanceTo(buffer.GetPosition(toCopy), buffer.End);
        }
    }

    private sealed class FakeLocalDataListener : ILocalDataListener
    {
        public int TcpPort => 0;
        public int QuicPort => 0;
        public bool SupportsQuic => false;
        public int SendCallCount { get; private set; }
        public byte[]? LastPipeData { get; private set; }

        public Task StartListeningAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task StopListeningAsync() => Task.CompletedTask;

        public async Task SendAsync(LocalDataTransportProtocol protocol, PipeReader payloadReader, IPEndPoint remoteEndPoint,
            string? remoteIdentityPublicKey = null, CancellationToken token = default)
        {
            SendCallCount++;
            using var ms = new MemoryStream();
            while (true)
            {
                var result = await payloadReader.ReadAsync(token);
                var buffer = result.Buffer;
                foreach (var segment in buffer)
                {
                    ms.Write(segment.Span);
                }

                payloadReader.AdvanceTo(buffer.End);
                if (result.IsCompleted)
                {
                    break;
                }
            }
            LastPipeData = ms.ToArray();
        }

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

    private sealed class NonReadableStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position { get; set; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => 0;
        public override void SetLength(long value) { }
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    #endregion
}
