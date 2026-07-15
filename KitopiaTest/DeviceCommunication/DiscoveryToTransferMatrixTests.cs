using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kitopia.Feature.DeviceCommunication;
using Kitopia.Feature.DeviceCommunication.Discovery;
using Kitopia.Feature.DeviceCommunication.Identity;
using Kitopia.Feature.DeviceCommunication.Protocol;
using Kitopia.Feature.DeviceCommunication.Transport;
using ObservableCollections;


namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class DiscoveryToTransferMatrixTests
{
    [TestMethod]
    public void DeviceCommunicationSettings_DefaultOperatingSystemName_IsUnknown()
    {
        IDeviceCommunicationSettings settings = new SettingsWithDefaultOperatingSystemName();

        Assert.AreEqual("Unknown", settings.OperatingSystemName);
    }

    [TestMethod]
    public void Discovery_OperatingSystemName_UsesHostProvidedSetting()
    {
        var localIdentity = CreateIdentity();
        using var discoveryService = new DeviceDiscoveryService(
            new FakeDeviceCommunicationSettings("  TestOS  "),
            new FakeIdentityStore(localIdentity),
            new FakeLocalDataEndpointProvider(23001));

        var operatingSystemName = InvokePrivateResult<string>(discoveryService, "ResolveOperatingSystemName");

        Assert.AreEqual("TestOS", operatingSystemName);
    }

    [TestMethod]
    [DataRow(22001)]
    public async Task DeviceTransportService_AlwaysUsesTcp(int tcpPort)
    {
        var listener = new RecordingLocalDataListener();
        var discoveryService = new FakeDeviceDiscoveryService();
        discoveryService.AddDevice(new DiscoveredDevice
        {
            Id = "peer-1",
            Ipv4Address = IPAddress.Loopback,
            TcpPort = tcpPort
        });
        var transport = new DeviceTransportService(listener, discoveryService);

        await transport.SendAsync("peer-1", new DataEnvelope { Route = "chat", Command = "text" });

        Assert.AreEqual(LocalDataTransportProtocol.Tcp, listener.Attempts.Single().Protocol);
    }

    [TestMethod]
    public void Discovery_AuthFailed_DoesNotPublishDevice()
    {
        var localIdentity = CreateIdentity();
        using var discoveryService = new DeviceDiscoveryService(
            new FakeDeviceCommunicationSettings(),
            new FakeIdentityStore(localIdentity),
            new FakeLocalDataEndpointProvider(23001));
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
            TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            PublicKey = remoteIdentity.PublicKey,
            Nonce = nonce,
            Signature = Convert.ToBase64String([1, 2, 3])
        };

        var localHash = ComputePublicKeyHash(localIdentity.PublicKey);
        InvokePrivateVoid(discoveryService, "HandleAuthResponse", response, remoteAddress, localIdentity.PublicKey, localHash);

        Assert.AreEqual(0, discoveryService.Devices.Count);
    }

    [TestMethod]
    public async Task Discovery_KnownDeviceAnnouncementWithSameEndpoint_RefreshesWithoutAuthRequest()
    {
        var localIdentity = CreateIdentity();
        using var discoveryService = new DeviceDiscoveryService(
            new FakeDeviceCommunicationSettings(),
            new FakeIdentityStore(localIdentity),
            new FakeLocalDataEndpointProvider(23001));
        var remoteIdentity = CreateIdentity();
        var remoteHash = ComputePublicKeyHash(remoteIdentity.PublicKey);
        var remoteAddress = IPAddress.Loopback;
        var nonce = Guid.NewGuid().ToString("N");

        InvokePrivateVoid(discoveryService, "RegisterPendingAuthRequest", remoteHash, nonce, remoteAddress);
        var response = new DiscoveryInfo
        {
            MessageType = "auth.response",
            Version = "0.1",
            Id = remoteHash,
            Name = "peer",
            OperatingSystem = "Windows",
            TcpPort = 23001,
            TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            PublicKey = remoteIdentity.PublicKey,
            Nonce = nonce
        };
        Assert.IsTrue(Kitopia.Feature.DeviceCommunication.Discovery.DeviceDiscoverySignature.TrySign(
            response,
            remoteIdentity.PrivateKey,
            out var signature));
        response.Signature = signature;

        var localHash = ComputePublicKeyHash(localIdentity.PublicKey);
        InvokePrivateVoid(discoveryService, "HandleAuthResponse", response, remoteAddress, localIdentity.PublicKey, localHash);
        Assert.AreEqual(1, discoveryService.Devices.Count);
        var device = discoveryService.Devices[0];
        device.LastSeen = DateTime.UtcNow - TimeSpan.FromSeconds(10);
        var previousLastSeen = device.LastSeen;

        var announcement = new DiscoveryInfo
        {
            MessageType = "announce",
            Version = "0.1",
            Id = remoteHash,
            Name = "peer-renamed",
            OperatingSystem = "Linux",
            TcpPort = 23001,
            TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        await InvokePrivateTask(discoveryService, "HandleAnnouncementAsync", announcement, remoteAddress, localHash, CancellationToken.None);

        Assert.AreEqual(1, discoveryService.Devices.Count);
        Assert.AreEqual(0, GetPendingAuthRequestCount(discoveryService));
        Assert.AreEqual("peer-renamed", device.Name);
        Assert.AreEqual("Linux", device.OperatingSystem);
        Assert.AreEqual(23001, device.TcpPort);
        Assert.IsTrue(device.LastSeen > previousLastSeen);
    }

    [TestMethod]
    public void Discovery_AuthenticatedResponse_PublishesDeviceOperatingSystem()
    {
        var localIdentity = CreateIdentity();
        using var discoveryService = new DeviceDiscoveryService(
            new FakeDeviceCommunicationSettings(),
            new FakeIdentityStore(localIdentity),
            new FakeLocalDataEndpointProvider(23001));
        var remoteIdentity = CreateIdentity();
        var remoteHash = ComputePublicKeyHash(remoteIdentity.PublicKey);
        var remoteAddress = IPAddress.Loopback;
        var nonce = Guid.NewGuid().ToString("N");

        InvokePrivateVoid(discoveryService, "RegisterPendingAuthRequest", remoteHash, nonce, remoteAddress);
        var response = new DiscoveryInfo
        {
            MessageType = "auth.response",
            Version = "0.1",
            Id = remoteHash,
            Name = "peer",
            OperatingSystem = "Android",
            TcpPort = 23001,
            TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            PublicKey = remoteIdentity.PublicKey,
            Nonce = nonce
        };
        Assert.IsTrue(Kitopia.Feature.DeviceCommunication.Discovery.DeviceDiscoverySignature.TrySign(
            response,
            remoteIdentity.PrivateKey,
            out var signature));
        response.Signature = signature;

        var localHash = ComputePublicKeyHash(localIdentity.PublicKey);
        InvokePrivateVoid(discoveryService, "HandleAuthResponse", response, remoteAddress, localIdentity.PublicKey, localHash);

        Assert.AreEqual(1, discoveryService.Devices.Count);
        Assert.AreEqual("Android", discoveryService.Devices[0].OperatingSystem);
    }

    [TestMethod]
    public async Task Discovery_KnownDeviceAnnouncementWithChangedPort_RequiresAuthRequest()
    {
        var localIdentity = CreateIdentity();
        using var discoveryService = new DeviceDiscoveryService(
            new FakeDeviceCommunicationSettings(),
            new FakeIdentityStore(localIdentity),
            new FakeLocalDataEndpointProvider(23001));
        var remoteIdentity = CreateIdentity();
        var remoteHash = ComputePublicKeyHash(remoteIdentity.PublicKey);
        var remoteAddress = IPAddress.Loopback;
        var nonce = Guid.NewGuid().ToString("N");

        InvokePrivateVoid(discoveryService, "RegisterPendingAuthRequest", remoteHash, nonce, remoteAddress);
        var response = new DiscoveryInfo
        {
            MessageType = "auth.response",
            Version = "0.1",
            Id = remoteHash,
            Name = "peer",
            TcpPort = 23001,
            TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            PublicKey = remoteIdentity.PublicKey,
            Nonce = nonce
        };
        Assert.IsTrue(Kitopia.Feature.DeviceCommunication.Discovery.DeviceDiscoverySignature.TrySign(
            response,
            remoteIdentity.PrivateKey,
            out var signature));
        response.Signature = signature;

        var localHash = ComputePublicKeyHash(localIdentity.PublicKey);
        InvokePrivateVoid(discoveryService, "HandleAuthResponse", response, remoteAddress, localIdentity.PublicKey, localHash);
        Assert.AreEqual(1, discoveryService.Devices.Count);
        var device = discoveryService.Devices[0];
        device.LastSeen = DateTime.UtcNow - TimeSpan.FromSeconds(10);
        var previousLastSeen = device.LastSeen;

        var announcement = new DiscoveryInfo
        {
            MessageType = "announce",
            Version = "0.1",
            Id = remoteHash,
            Name = "peer-renamed",
            TcpPort = 23002,
            TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        await InvokePrivateTask(discoveryService, "HandleAnnouncementAsync", announcement, remoteAddress, localHash, CancellationToken.None);

        Assert.AreEqual(1, discoveryService.Devices.Count);
        Assert.AreEqual(1, GetPendingAuthRequestCount(discoveryService));
        Assert.AreEqual("peer", device.Name);
        Assert.AreEqual(23001, device.TcpPort);
        Assert.AreEqual(previousLastSeen, device.LastSeen);
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

    private static async Task InvokePrivateTask(object instance, string methodName, params object[] args)
    {
        var method = instance.GetType().GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.IsNotNull(method, $"Method '{methodName}' not found.");
        var result = method.Invoke(instance, args);
        if (result is not Task task)
        {
            Assert.Fail($"Method '{methodName}' did not return a task.");
            return;
        }

        await task;
    }

    private static T InvokePrivateResult<T>(object instance, string methodName, params object[] args)
    {
        var method = instance.GetType().GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.IsNotNull(method, $"Method '{methodName}' not found.");
        var result = method.Invoke(instance, args);
        Assert.IsInstanceOfType<T>(result);
        return (T)result!;
    }

    private static int GetPendingAuthRequestCount(object instance)
    {
        var field = instance.GetType().GetField(
            "_pendingAuthRequests",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.IsNotNull(field, "Pending auth request field not found.");
        var value = field.GetValue(instance);
        Assert.IsInstanceOfType<System.Collections.IDictionary>(value);
        return ((System.Collections.IDictionary)value).Count;
    }

    private sealed class FakeDeviceDiscoveryService : IDeviceDiscoveryService
    {
        private readonly ObservableList<DiscoveredDevice> _devicesSource = [];
        private readonly ISynchronizedView<DiscoveredDevice, DiscoveredDevice> _devicesView;

        public FakeDeviceDiscoveryService()
        {
            _devicesView = _devicesSource.CreateView(device => device);
            Devices = _devicesView.ToNotifyCollectionChanged();
        }

        public NotifyCollectionChangedSynchronizedViewList<DiscoveredDevice> Devices { get; }

        public Task StartAsync(CancellationToken token) => Task.CompletedTask;

        public Task StopAsync() => Task.CompletedTask;

        public void AddDevice(DiscoveredDevice device)
        {
            _devicesSource.Add(device);
        }

        public void Dispose()
        {
            Devices.Dispose();
            _devicesView.Dispose();
        }
    }

    private sealed class FakeIdentityStore : IDeviceIdentityStore
    {
        private readonly DeviceIdentity _identity;

        public FakeIdentityStore((string PublicKey, string PrivateKey) identity)
        {
            _identity = new DeviceIdentity(
                identity.PublicKey,
                identity.PrivateKey,
                ComputePublicKeyHash(identity.PublicKey));
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

    private sealed class FakeDeviceCommunicationSettings : Kitopia.Feature.DeviceCommunication.Discovery.IDeviceCommunicationSettings
    {
        public FakeDeviceCommunicationSettings(string operatingSystemName = "Unknown")
        {
            OperatingSystemName = operatingSystemName;
        }

        public string BroadcastName => string.Empty;
        public string OperatingSystemName { get; }

        public string? GetCustomName(string publicKey)
        {
            return null;
        }

        public void SetCustomName(string publicKey, string name) { }

        public void RemoveCustomName(string publicKey) { }
    }

    private sealed class SettingsWithDefaultOperatingSystemName : IDeviceCommunicationSettings
    {
        public string BroadcastName => string.Empty;

        public string? GetCustomName(string publicKey) => null;

        public void SetCustomName(string publicKey, string name) { }

        public void RemoveCustomName(string publicKey) { }
    }

    private sealed class FakeLocalDataEndpointProvider : Kitopia.Feature.DeviceCommunication.Transport.ILocalDataEndpointProvider
    {
        public FakeLocalDataEndpointProvider(int tcpPort)
        {
            TcpPort = tcpPort;
        }

        public int TcpPort { get; }
    }

    private sealed class RecordingLocalDataListener : ILocalDataListener
    {
        public int TcpPort => 0;
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
