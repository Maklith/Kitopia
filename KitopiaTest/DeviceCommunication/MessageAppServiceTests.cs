using System.IO.Pipelines;
using System.Net;
using System.Buffers.Binary;
using System.Text.Json;
using Core.Services.DeviceCommunication;
using Core.Services.DeviceCommunication.Application;
using Core.Services.DeviceCommunication.Codecs;
using Core.Services.DeviceCommunication.Handlers;
using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Routing;
using Core.Services.DeviceCommunication.Sessions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class MessageAppServiceTests
{
    [TestMethod]
    public async Task SendTextChatAsync_SerializesAndSendsEnvelope()
    {
        var listener = new FakeLocalDataListener();
        var sender = new ProtocolSender(listener);
        var registry = new MessageCodecRegistry(new IMessageCodec[] { new ChatMessageCodec() });
        var service = new MessageAppService(
            registry,
            sender,
            new IncomingMessageBuffer(),
            new ImageTransferPolicy(),
            new FileTransferSessionStore());

        var context = new MessageContext(
            LocalDataTransportProtocol.Tcp,
            new IPEndPoint(IPAddress.Loopback, 45000),
            "peer-key");

        await service.SendTextChatAsync(context, new TextChatMessage("peer-1", "hello"));

        Assert.AreEqual(1, listener.SendCount);
        Assert.IsNotNull(listener.LastPayload);
        var span = listener.LastPayload!.Value.Span;
        var envelopeLength = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4));
        var envelope = JsonSerializer.Deserialize<DataEnvelope>(span.Slice(16, envelopeLength));
        Assert.IsNotNull(envelope);
        Assert.AreEqual("chat", envelope.Route);
        Assert.AreEqual("text", envelope.Command);
    }

    private sealed class FakeLocalDataListener : ILocalDataListener
    {
        public int TcpPort => 0;
        public int QuicPort => 0;
        public bool SupportsQuic => false;
        public int SendCount { get; private set; }
        public ReadOnlyMemory<byte>? LastPayload { get; private set; }

        public Task StartListeningAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task StopListeningAsync() => Task.CompletedTask;

        public Task SendAsync(LocalDataTransportProtocol protocol, ReadOnlyMemory<byte> payload, IPEndPoint remoteEndPoint,
            string? remoteIdentityPublicKey = null, CancellationToken token = default)
        {
            _ = protocol;
            _ = remoteEndPoint;
            _ = remoteIdentityPublicKey;
            _ = token;
            SendCount++;
            LastPayload = payload.ToArray();
            return Task.CompletedTask;
        }

        public Task SendAsync(LocalDataTransportProtocol protocol, PipeReader payloadReader, IPEndPoint remoteEndPoint,
            string? remoteIdentityPublicKey = null, CancellationToken token = default)
        {
            throw new NotSupportedException();
        }

        public Task SendAsync(LocalDataTransportProtocol protocol, Stream stream, IPEndPoint remoteEndPoint,
            string? remoteIdentityPublicKey = null, CancellationToken token = default)
        {
            _ = protocol;
            _ = remoteEndPoint;
            _ = remoteIdentityPublicKey;
            _ = token;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            SendCount++;
            LastPayload = memory.ToArray();
            return Task.CompletedTask;
        }
    }
}
