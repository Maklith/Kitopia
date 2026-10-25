using System.ComponentModel;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using Kitopia.Feature.DeviceCommunication.Discovery;
using Kitopia.Feature.DeviceCommunication.Identity;
using Kitopia.Feature.DeviceCommunication.Protocol;
using Kitopia.Feature.DeviceCommunication.Routing;
using Kitopia.Feature.DeviceCommunication.Security;
using Kitopia.Feature.DeviceCommunication.Transport;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class SharedLoopbackTransportTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TcpTransport_SilentPeer_RespectsHandshakeTimeoutAndCallerCancellation(bool cancel)
    {
        using var sender = new TcpLocalDataListener(
            new ProtocolSession((_, _, _) => ValueTask.CompletedTask),
            new DeviceTransportSecurity(new FakeIdentityStore(CreateIdentity())),
            new LoopbackIdentityResolver(string.Empty));
        Assert.IsTrue(await sender.StartAsync());
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var send = sender.SendAsync(Encoding.UTF8.GetBytes("hello"),
            (IPEndPoint)listener.LocalEndpoint, CreateIdentity().PublicKey, cancellation.Token);
        using var peer = await listener.AcceptTcpClientAsync(cancellation.Token);

        if (cancel)
        {
            await cancellation.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => send);
        }
        else
        {
            await Assert.ThrowsAsync<TimeoutException>(() => send);
        }
    }

    [TestMethod]
    public async Task TcpTransport_UnexpectedServerIdentity_RejectsHandshakeBeforeSendingPayload()
    {
        var senderIdentity = CreateIdentity();
        var receiverIdentity = CreateIdentity();
        var unexpectedIdentity = CreateIdentity();
        var dispatched = false;
        using var sender = new TcpLocalDataListener(
            new ProtocolSession((_, _, _) => ValueTask.CompletedTask),
            new DeviceTransportSecurity(new FakeIdentityStore(senderIdentity)),
            new LoopbackIdentityResolver(receiverIdentity.PublicKey));
        using var receiver = new TcpLocalDataListener(
            new ProtocolSession((_, _, _) =>
            {
                dispatched = true;
                return ValueTask.CompletedTask;
            }),
            new DeviceTransportSecurity(new FakeIdentityStore(receiverIdentity)),
            new LoopbackIdentityResolver(senderIdentity.PublicKey));
        Assert.IsTrue(await sender.StartAsync());
        Assert.IsTrue(await receiver.StartAsync());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await Assert.ThrowsAsync<AuthenticationException>(() => sender.SendAsync(
            Encoding.UTF8.GetBytes("must not be sent"),
            new IPEndPoint(IPAddress.Loopback, receiver.Port),
            unexpectedIdentity.PublicKey,
            cancellation.Token));

        Assert.IsFalse(dispatched);
    }

    [TestMethod]
    [DataRow(SslProtocols.Tls12, false)]
    [DataRow(SslProtocols.Tls12, true)]
    [DataRow(SslProtocols.Tls13, false)]
    [DataRow(SslProtocols.Tls13, true)]
    public async Task TcpTransport_TlsVersion_AuthenticatesDeviceAndSendsPayload(
        SslProtocols protocols, bool usePipeReader)
    {
        var senderIdentity = CreateIdentity();
        var receiverIdentity = CreateIdentity();
        var receiverSecurity = new DeviceTransportSecurity(new FakeIdentityStore(receiverIdentity));
        using var certificate = receiverSecurity.CreateIdentityCertificate("CN=Kitopia-Receiver");
        using var sender = new TcpLocalDataListener(
            new ProtocolSession((_, _, _) => ValueTask.CompletedTask),
            new DeviceTransportSecurity(new FakeIdentityStore(senderIdentity)),
            new LoopbackIdentityResolver(receiverIdentity.PublicKey));
        Assert.IsTrue(await sender.StartAsync());
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var payload = Encoding.UTF8.GetBytes("hello from authenticated device");
        var receivedCertificate = false;
        var receiveTask = ReceiveAsync();

        if (usePipeReader)
        {
            using var stream = new MemoryStream(payload);
            var reader = PipeReader.Create(stream);
            try
            {
                await Task.WhenAll(receiveTask,
                    sender.SendAsync(reader, endpoint, receiverIdentity.PublicKey, cancellation.Token));
            }
            finally
            {
                await reader.CompleteAsync();
            }
        }
        else
        {
            await Task.WhenAll(receiveTask,
                sender.SendAsync(payload, endpoint, receiverIdentity.PublicKey, cancellation.Token));
        }

        var received = await receiveTask;
        Assert.IsTrue(receivedCertificate);
        CollectionAssert.AreEqual(payload, received);

        async Task<byte[]> ReceiveAsync()
        {
            using var client = await listener.AcceptTcpClientAsync(cancellation.Token);
            await using var stream = new SslStream(client.GetStream());
            await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificateContext = SslStreamCertificateContext.Create(certificate, null, offline: true),
                ClientCertificateRequired = true,
                EnabledSslProtocols = protocols,
                ApplicationProtocols = [new SslApplicationProtocol("kitopia-local-data")],
                RemoteCertificateValidationCallback = (_, remoteCertificate, _, _) =>
                    receivedCertificate = receiverSecurity.ValidateRemoteCertificate(remoteCertificate, senderIdentity.PublicKey)
            }, cancellation.Token);
            var received = new byte[payload.Length];
            await stream.ReadExactlyAsync(received, cancellation.Token);
            await stream.ShutdownAsync();
            return received;
        }
    }

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
