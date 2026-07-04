using System.IO.Pipelines;
using System.Net;
using System.ComponentModel;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using Kitopia.DeviceCommunication.Discovery;
using Kitopia.DeviceCommunication.Identity;
using Kitopia.DeviceCommunication.Protocol;
using Kitopia.DeviceCommunication.Routing;
using Kitopia.DeviceCommunication.Security;
using Kitopia.DeviceCommunication.Transport;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class SharedLoopbackTransportTests
{
    [TestMethod]
    public async Task TcpTransport_SendsEnvelopeAndPayload_BetweenTwoSharedServiceInstances()
    {
        var senderIdentity = CreateIdentity();
        var receiverIdentity = CreateIdentity();
        var payload = Encoding.UTF8.GetBytes("hello from shared transport");
        var envelopeReceived = new TaskCompletionSource<DataEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var payloadReceived = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var senderHost = new LocalDataListenerHost(
            new ProtocolSession((_, _, _) => ValueTask.CompletedTask),
            new DeviceTransportSecurity(new FakeIdentityStore(senderIdentity)),
            new LoopbackIdentityResolver(receiverIdentity.PublicKey));
        using var receiverHost = new LocalDataListenerHost(
            new ProtocolSession(async (envelope, reader, cancellationToken) =>
            {
                envelopeReceived.TrySetResult(envelope);
                var actualPayload = await LocalDataPipeIo.ReadExactlyAsync(reader, payload.Length, cancellationToken);
                payloadReceived.TrySetResult(actualPayload);
            }),
            new DeviceTransportSecurity(new FakeIdentityStore(receiverIdentity)),
            new LoopbackIdentityResolver(senderIdentity.PublicKey));

        await senderHost.StartListeningAsync();
        await receiverHost.StartListeningAsync();

        var sender = new ProtocolSender((reader, cancellationToken) =>
            senderHost.SendAsync(
                LocalDataTransportProtocol.Tcp,
                reader,
                new IPEndPoint(IPAddress.Loopback, receiverHost.TcpPort),
                receiverIdentity.PublicKey,
                cancellationToken));
        var envelope = new DataEnvelope
        {
            Route = "chat",
            Command = "text",
            StreamType = DataStreamType.Text,
            ContentType = "text/plain",
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["senderId"] = senderIdentity.PublicKey,
                ["conversationId"] = receiverIdentity.PublicKey
            }
        };

        try
        {
            await sender.SendAsync(envelope, new MemoryStream(payload));
        }
        catch (AuthenticationException ex) when (ex.InnerException is Win32Exception { NativeErrorCode: unchecked((int)0x8009030E) })
        {
            Assert.Inconclusive("Current hosted Windows test environment cannot acquire Schannel credentials for local SslStream loopback handshakes (SEC_E_NO_CREDENTIALS).");
            return;
        }

        var actualEnvelope = await envelopeReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var actualPayload = await payloadReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.AreEqual("chat", actualEnvelope.Route);
        Assert.AreEqual("text", actualEnvelope.Command);
        Assert.AreEqual(DataStreamType.Text, actualEnvelope.StreamType);
        Assert.AreEqual("text/plain", actualEnvelope.ContentType);
        Assert.AreEqual(senderIdentity.PublicKey, actualEnvelope.Metadata?["senderId"]);
        CollectionAssert.AreEqual(payload, actualPayload);
    }

    private static DeviceIdentity CreateIdentity()
    {
        using var rsa = RSA.Create(2048);
        var publicKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        var privateKey = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());
        return new DeviceIdentity(
            publicKey,
            privateKey,
            DeviceDiscoverySignature.ComputePublicKeyHash(publicKey));
    }

    private sealed class FakeIdentityStore : IDeviceIdentityStore
    {
        private readonly DeviceIdentity _identity;

        public FakeIdentityStore(DeviceIdentity identity)
        {
            _identity = identity;
        }

        public bool TryGetIdentity(out DeviceIdentity identity)
        {
            identity = _identity;
            return true;
        }

        public DeviceIdentity EnsureIdentity()
        {
            return _identity;
        }
    }

    private sealed class LoopbackIdentityResolver : IRemoteIdentityResolver
    {
        private readonly string _expectedPublicKey;

        public LoopbackIdentityResolver(string expectedPublicKey)
        {
            _expectedPublicKey = expectedPublicKey;
        }

        public string? ResolveExpectedIdentityPublicKey(IPEndPoint remoteEndPoint)
        {
            _ = remoteEndPoint;
            return _expectedPublicKey;
        }
    }
}
