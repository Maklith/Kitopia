using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Core.Services.Config;
using Core.Services.DeviceCommunication;
using Core.Services.DeviceCommunication.Discovery;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Routing;
using ObservableCollections;
using PluginCore;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
[DoNotParallelize]
public sealed class DiscoveryToTransferMatrixTests
{
    private Dictionary<string, PluginCore.Config.ConfigBase>? _originalConfigs;

    [TestInitialize]
    public void TestInitialize()
    {
        _originalConfigs = ConfigManger.Configs;
        ConfigManger.Configs = new Dictionary<string, PluginCore.Config.ConfigBase>(StringComparer.Ordinal)
        {
            ["KitopiaConfig"] = new KitopiaConfig { Name = "KitopiaConfig" }
        };
    }

    [TestCleanup]
    public void TestCleanup()
    {
        if (_originalConfigs is not null)
        {
            ConfigManger.Configs = _originalConfigs;
        }
    }

    [TestMethod]
    [DataRow(true, true)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(false, false)]
    public async Task DeviceTransportService_SelectsQuicOnlyWhenBothSidesSupportIt(
        bool localSupportsQuic,
        bool remoteSupportsQuic)
    {
        var listener = new RecordingLocalDataListener { SupportsQuicValue = localSupportsQuic };
        var discoveryService = new FakeDeviceDiscoveryService();
        discoveryService.AddDevice(new DeviceModel
        {
            Id = "peer-1",
            Ipv4Address = IPAddress.Loopback,
            TcpPort = 22001,
            QuicPort = remoteSupportsQuic ? 22002 : 0,
            SupportQuic = remoteSupportsQuic
        });
        var transport = new DeviceTransportService(listener, new ProtocolSender(listener), discoveryService);

        await transport.SendAsync("peer-1", new DataEnvelope { Route = "chat", Command = "text" });

        var expected = localSupportsQuic && remoteSupportsQuic
            ? LocalDataTransportProtocol.Quic
            : LocalDataTransportProtocol.Tcp;
        Assert.AreEqual(expected, listener.Attempts.Single().Protocol);
    }

    [TestMethod]
    public async Task DeviceTransportService_QuicFailure_FallsBackToTcp()
    {
        var listener = new RecordingLocalDataListener { SupportsQuicValue = true, FailQuicSend = true };
        var discoveryService = new FakeDeviceDiscoveryService();
        discoveryService.AddDevice(new DeviceModel
        {
            Id = "peer-1",
            Ipv4Address = IPAddress.Loopback,
            TcpPort = 22001,
            QuicPort = 22002,
            SupportQuic = true
        });
        var transport = new DeviceTransportService(listener, new ProtocolSender(listener), discoveryService);

        await transport.SendAsync("peer-1", new DataEnvelope { Route = "chat", Command = "text" });

        CollectionAssert.AreEqual(
            new[] { LocalDataTransportProtocol.Quic, LocalDataTransportProtocol.Tcp },
            listener.Attempts.Select(attempt => attempt.Protocol).ToArray());
    }

    [TestMethod]
    public void Discovery_AuthFailed_DoesNotPublishDevice()
    {
        var localIdentity = CreateIdentity();
        ConfigManger.Config.devicePrivateKey = localIdentity.PrivateKey;
        ConfigManger.Config.EnsureDeviceIdentity();

        using var discoveryService = new DeviceDiscoveryService();
        var remoteIdentity = CreateIdentity();
        var remoteHash = ComputePublicKeyHash(remoteIdentity.PublicKey);
        var nonce = Guid.NewGuid().ToString("N");
        var remoteAddress = IPAddress.Loopback;

        InvokePrivateVoid(discoveryService, "RegisterPendingAuthRequest", remoteHash, nonce, remoteAddress);

        var response = new DiscoveryInfo
        {
            MessageType = "auth.response",
            Version = "0.1",
            Id = remoteHash,
            Name = "peer-fail",
            TcpPort = 23001,
            SupportsQuic = false,
            QuicPort = 0,
            TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            PublicKey = remoteIdentity.PublicKey,
            Nonce = nonce,
            Signature = Convert.ToBase64String([1, 2, 3])
        };

        var localHash = ComputePublicKeyHash(localIdentity.PublicKey);
        InvokePrivateVoid(discoveryService, "HandleAuthResponse", response, remoteAddress, localIdentity.PublicKey, localHash);

        Assert.AreEqual(0, discoveryService.Devices.Count);
    }

    private static string ComputePublicKeyHash(string publicKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(publicKey.Trim()));
        return Convert.ToHexString(hash);
    }

    private static (string PublicKey, string PrivateKey) CreateIdentity()
    {
        using var rsa = RSA.Create(2048);
        var publicKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        var privateKey = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());
        return (publicKey, privateKey);
    }

    private static void InvokePrivateVoid(object instance, string methodName, params object[] args)
    {
        var method = instance.GetType().GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.IsNotNull(method, $"Method '{methodName}' not found.");
        method.Invoke(instance, args);
    }

    private sealed class FakeDeviceDiscoveryService : IDeviceDiscoveryService
    {
        private readonly ObservableList<DeviceModel> _devicesSource = [];
        private readonly ISynchronizedView<DeviceModel, DeviceModel> _devicesView;

        public FakeDeviceDiscoveryService()
        {
            _devicesView = _devicesSource.CreateView(device => device);
            Devices = _devicesView.ToNotifyCollectionChanged();
        }

        public NotifyCollectionChangedSynchronizedViewList<DeviceModel> Devices { get; }

        public Task StartAsync(CancellationToken token) => Task.CompletedTask;

        public Task StopAsync() => Task.CompletedTask;

        public void AddDevice(DeviceModel device)
        {
            _devicesSource.Add(device);
        }

        public void Dispose()
        {
            Devices.Dispose();
            _devicesView.Dispose();
        }
    }

    private sealed class RecordingLocalDataListener : ILocalDataListener
    {
        public int TcpPort => 0;
        public int QuicPort => 0;
        public bool SupportsQuic => SupportsQuicValue;
        public bool SupportsQuicValue { get; init; }
        public bool FailQuicSend { get; set; }
        public List<(LocalDataTransportProtocol Protocol, IPEndPoint EndPoint, DataEnvelope Envelope)> Attempts { get; } = [];

        public Task StartListeningAsync(CancellationToken token = default) => Task.CompletedTask;

        public Task StopListeningAsync() => Task.CompletedTask;

        public async Task SendAsync(
            LocalDataTransportProtocol protocol,
            PipeReader payloadReader,
            IPEndPoint remoteEndPoint,
            string? remoteIdentityPublicKey = null,
            CancellationToken token = default)
        {
            Attempts.Add((protocol, remoteEndPoint, new DataEnvelope()));
            if (protocol == LocalDataTransportProtocol.Quic && FailQuicSend)
            {
                FailQuicSend = false;
                throw new IOException("quic failed");
            }

            using var memory = new MemoryStream();
            await payloadReader.CopyToAsync(memory, token);
            var frame = memory.ToArray();
            var envelopeLength = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(4, 4));
            var envelope = JsonSerializer.Deserialize<DataEnvelope>(frame.AsSpan(16, envelopeLength));
            Assert.IsNotNull(envelope);
            Attempts[^1] = (protocol, remoteEndPoint, envelope);
        }

        public Task SendAsync(
            LocalDataTransportProtocol protocol,
            ReadOnlyMemory<byte> payload,
            IPEndPoint remoteEndPoint,
            string? remoteIdentityPublicKey = null,
            CancellationToken token = default)
        {
            return SendAsync(protocol, PipeReader.Create(new MemoryStream(payload.ToArray())), remoteEndPoint,
                remoteIdentityPublicKey, token);
        }

        public Task SendAsync(
            LocalDataTransportProtocol protocol,
            Stream stream,
            IPEndPoint remoteEndPoint,
            string? remoteIdentityPublicKey = null,
            CancellationToken token = default)
        {
            return SendAsync(protocol, PipeReader.Create(stream), remoteEndPoint, remoteIdentityPublicKey, token);
        }
    }
}
