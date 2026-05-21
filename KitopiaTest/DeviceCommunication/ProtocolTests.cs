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
    #region FrameProtocol

    [TestMethod]
    public void TryReadFrameHeader_ReturnsFalse_WhenBufferTooShort()
    {
        var source = new byte[FrameProtocol.HeaderLength - 1];
        var ok = FrameProtocol.TryReadFrameHeader(source, out _, out _);
        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void TryReadFrameHeader_ReturnsFalse_WhenEmptyBuffer()
    {
        var ok = FrameProtocol.TryReadFrameHeader(ReadOnlySpan<byte>.Empty, out _, out _);
        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void TryReadFrameHeader_ReturnsFalse_WhenPayloadLengthNegative()
    {
        var bytes = new byte[FrameProtocol.HeaderLength];
        bytes[0] = FrameProtocol.CurrentVersion;
        bytes[1] = 1;
        bytes[2] = 0;
        BitConverter.GetBytes(-1).CopyTo(bytes, 19);

        var ok = FrameProtocol.TryReadFrameHeader(bytes, out _, out _);
        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void TryReadFrameHeader_AllowsZeroPayloadLength()
    {
        var header = new FrameHeader(FrameProtocol.CurrentVersion, 1, 0, Guid.NewGuid(), 0);
        var bytes = new byte[FrameProtocol.HeaderLength];
        FrameProtocol.WriteFrameHeader(bytes, header);

        var ok = FrameProtocol.TryReadFrameHeader(bytes, out var actual, out var consumed);
        Assert.IsTrue(ok);
        Assert.AreEqual(FrameProtocol.HeaderLength, consumed);
        Assert.AreEqual(header, actual);
    }

    [TestMethod]
    public void WriteAndReadFrameHeader_RoundTrips()
    {
        var expected = new FrameHeader(
            Version: FrameProtocol.CurrentVersion,
            FrameType: 2,
            Flags: 1,
            ChannelId: Guid.NewGuid(),
            PayloadLength: 128);

        var bytes = new byte[FrameProtocol.HeaderLength];
        FrameProtocol.WriteFrameHeader(bytes, expected);

        var ok = FrameProtocol.TryReadFrameHeader(bytes, out var actual, out var consumed);
        Assert.IsTrue(ok);
        Assert.AreEqual(FrameProtocol.HeaderLength, consumed);
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void WriteFrameHeader_Throws_WhenBufferTooSmall()
    {
        var header = new FrameHeader(1, 1, 0, Guid.NewGuid(), 10);
        var tooSmall = new byte[FrameProtocol.HeaderLength - 1];
        Assert.ThrowsExactly<ArgumentException>(() => FrameProtocol.WriteFrameHeader(tooSmall, header));
    }

    [TestMethod]
    public void WriteFrameHeader_PreservesAllFields()
    {
        var channelId = Guid.NewGuid();
        var header = new FrameHeader(Version: 3, FrameType: 7, Flags: 0xFF, ChannelId: channelId, PayloadLength: 999999);
        var bytes = new byte[FrameProtocol.HeaderLength];
        FrameProtocol.WriteFrameHeader(bytes, header);

        Assert.AreEqual(3, bytes[0]);
        Assert.AreEqual(7, bytes[1]);
        Assert.AreEqual(0xFF, bytes[2]);
        Assert.AreEqual(channelId, new Guid(bytes.AsSpan(3, 16)));
        Assert.AreEqual(999999, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(19, 4)));
    }

    #endregion

    #region ProtocolSender

    [TestMethod]
    public async Task SendEnvelopeAsync_SendsFrameWithMagicAndEnvelope()
    {
        var listener = new FakeLocalDataListener();
        var sender = new ProtocolSender(listener);
        var context = CreateContext();
        var envelope = new DataEnvelope { Route = "chat", Command = "text", StreamType = DataStreamType.Text };

        await sender.SendEnvelopeAsync(context, envelope);

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
    public async Task SendEnvelopeWithPayloadAsync_SendsFrameEnvelopeAndPayload()
    {
        var listener = new FakeLocalDataListener();
        var sender = new ProtocolSender(listener);
        var context = CreateContext();
        var envelope = new DataEnvelope { Route = "chat", Command = "file", StreamType = DataStreamType.File, ChannelId = Guid.NewGuid() };
        var payloadBytes = new byte[] { 1, 2, 3, 4, 5 };
        using var payloadStream = new MemoryStream(payloadBytes, writable: false);

        await sender.SendEnvelopeWithPayloadAsync(context, envelope, payloadStream);

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
    public async Task SendEnvelopeWithPayloadAsync_ReportsProgress()
    {
        var listener = new FakeLocalDataListener();
        var sender = new ProtocolSender(listener);
        var context = CreateContext();
        var envelope = new DataEnvelope { Route = "chat", Command = "file", StreamType = DataStreamType.File };
        var payloadBytes = new byte[2 * 1024 * 1024];
        Random.Shared.NextBytes(payloadBytes);
        using var payloadStream = new MemoryStream(payloadBytes, writable: false);

        var progressReports = new List<(long Sent, long Total)>();
        await sender.SendEnvelopeWithPayloadAsync(context, envelope, payloadStream,
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
    public async Task SendEnvelopeWithPayloadAsync_Throws_WhenStreamNotReadable()
    {
        var listener = new FakeLocalDataListener();
        var sender = new ProtocolSender(listener);
        var envelope = new DataEnvelope { Route = "chat", Command = "file", StreamType = DataStreamType.File };
        var unreadable = new NonReadableStream();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => sender.SendEnvelopeWithPayloadAsync(CreateContext(), envelope, unreadable));
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
        return new ProtocolSession(new DeviceMessageDispatcher(
            new MessageCodecRegistry(),
            sink,
            new FileTransferSessionStore()));
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
