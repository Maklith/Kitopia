using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ObservableCollections;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Kitopia.Desktop.Services;
using Kitopia.Feature.DeviceCommunication;
using Kitopia.Feature.DeviceCommunication.Diagnostics;
using Kitopia.Feature.DeviceCommunication.Discovery;
using Kitopia.Feature.DeviceCommunication.Identity;
using Kitopia.Feature.DeviceCommunication.Protocol;
using Kitopia.Feature.DeviceCommunication.Transport;


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
    public async Task Discovery_AuthFailed_DoesNotPublishDevice()
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
        await InvokePrivateTask(discoveryService, "HandleAuthResponseAsync", response, remoteAddress,
            localIdentity.PublicKey, localHash, CancellationToken.None);

        Assert.AreEqual(0, discoveryService.Devices.Count);
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow("{\"MessageType\":\"probe\",\"Version\":\"0.1\"}", true, "Received probe from", LogEventLevel.Debug)]
    [DataRow("null", true, "JSON payload is null", LogEventLevel.Warning)]
    [DataRow("{", true, "Receive loop error on", LogEventLevel.Error)]
    [DataRow("{\"MessageType\":\"probe\",\"Version\":\"9.9\"}", true, "unsupported protocol version", LogEventLevel.Warning)]
    [DataRow("{\"MessageType\":\"probe\",\"Version\":\"0.1\"}", false, "local device identity is unavailable", LogEventLevel.Warning)]
    [DataRow("{\"MessageType\":\"auth.request\",\"Version\":\"0.1\",\"Id\":\"different-device\",\"Nonce\":\"test-nonce\"}", true,
        "does not match local id", LogEventLevel.Debug)]
    public async Task Discovery_ReceiveLoop_ReportsProcessingOutcomeWithoutRawDatagramLog(
        string payload, bool identityAvailable, string expectedMessage, LogEventLevel expectedLevel)
    {
        var identityStore = new FakeIdentityStore(CreateIdentity()) { IsAvailable = identityAvailable };
        using var discoveryService = new DeviceDiscoveryService(
            new FakeDeviceCommunicationSettings(), identityStore, new FakeLocalDataEndpointProvider(23001));
        using var receiver = new UdpClient(AddressFamily.InterNetwork);
        receiver.ExclusiveAddressUse = true;
        receiver.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        using var sender = new UdpClient(AddressFamily.InterNetwork);
        using var cancellation = new CancellationTokenSource();
        var sink = new DiscoveryLogSink(expectedMessage);
        using var logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
        var previousDiagnostics = DeviceCommunicationDiagnostics.Current;
        Task? receiveTask = null;
        try
        {
            DeviceCommunicationDiagnostics.Current = new DesktopDeviceCommunicationDiagnostics(logger);
            receiveTask = InvokePrivateTask(discoveryService, "ReceiveLoopAsync", receiver, cancellation.Token);
            await sender.SendAsync(Encoding.UTF8.GetBytes(payload), (IPEndPoint)receiver.Client.LocalEndPoint!);
            var outcome = await sink.ExpectedEvent.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var events = sink.Events.ToArray();
            var receivedIndex = Array.FindIndex(events, entry =>
                entry.RenderMessage().StartsWith("UDP datagram received from", StringComparison.Ordinal));

            Assert.AreEqual(-1, receivedIndex, "Routine UDP receives must not emit per-datagram logs.");
            Assert.AreEqual(expectedLevel, outcome.Level);
            if (payload == "{") Assert.IsInstanceOfType<JsonException>(outcome.Exception);
            Assert.AreEqual(0, discoveryService.Devices.Count);
        }
        finally
        {
            try
            {
                await cancellation.CancelAsync();
                if (receiveTask is not null) await receiveTask.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                DeviceCommunicationDiagnostics.Current = previousDiagnostics;
            }
        }
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
        var requestedAddress = IPAddress.Loopback;
        var responseAddress = requestedAddress;
        var nonce = Guid.NewGuid().ToString("N");

        InvokePrivateVoid(discoveryService, "RegisterPendingAuthRequest", remoteHash, nonce, requestedAddress);
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
        await InvokePrivateTask(discoveryService, "HandleAuthResponseAsync", response, responseAddress,
            localIdentity.PublicKey, localHash, CancellationToken.None);
        Assert.AreEqual(1, discoveryService.Devices.Count);
        var device = discoveryService.Devices[0];
        var changedAddress = requestedAddress;
        var samePortAnnouncement = new DiscoveryInfo
        {
            MessageType = "announce",
            Version = "0.1",
            Id = remoteHash,
            Name = "peer-renamed",
            TcpPort = 23001,
            TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        await InvokePrivateTask(discoveryService, "HandleAnnouncementAsync", samePortAnnouncement, changedAddress,
            localHash, CancellationToken.None);

        Assert.AreEqual(0, GetPendingAuthRequestCount(discoveryService));
        Assert.AreEqual(changedAddress, device.Ipv4Address);

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

        await InvokePrivateTask(discoveryService, "HandleAnnouncementAsync", announcement, changedAddress, localHash, CancellationToken.None);

        Assert.AreEqual(1, discoveryService.Devices.Count);
        Assert.AreEqual(0, GetPendingAuthRequestCount(discoveryService));
        Assert.AreEqual("peer-renamed", device.Name);
        Assert.AreEqual("Linux", device.OperatingSystem);
        Assert.AreEqual(23001, device.TcpPort);
        Assert.IsTrue(device.LastSeen > previousLastSeen);
    }

    [TestMethod]
    public async Task Discovery_AuthenticatedResponse_PublishesDeviceOperatingSystem()
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
        await InvokePrivateTask(discoveryService, "HandleAuthResponseAsync", response, remoteAddress,
            localIdentity.PublicKey, localHash, CancellationToken.None);

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
        await InvokePrivateTask(discoveryService, "HandleAuthResponseAsync", response, remoteAddress,
            localIdentity.PublicKey, localHash, CancellationToken.None);
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

    [TestMethod]
    public async Task Discovery_UnverifiedAddressDoesNotReplaceVerifiedEndpoint()
    {
        var localIdentity = CreateIdentity();
        using var discoveryService = new DeviceDiscoveryService(
            new FakeDeviceCommunicationSettings(),
            new FakeIdentityStore(localIdentity),
            new FakeLocalDataEndpointProvider(23001));
        var remoteIdentity = CreateIdentity();
        var remoteHash = ComputePublicKeyHash(remoteIdentity.PublicKey);
        var verifiedAddress = IPAddress.Loopback;
        var nonce = Guid.NewGuid().ToString("N");

        InvokePrivateVoid(discoveryService, "RegisterPendingAuthRequest", remoteHash, nonce, verifiedAddress);
        var response = new DiscoveryInfo
        {
            MessageType = "auth.response",
            Version = "0.1",
            Id = remoteHash,
            TcpPort = 23001,
            TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            PublicKey = remoteIdentity.PublicKey,
            Nonce = nonce
        };
        Assert.IsTrue(DeviceDiscoverySignature.TrySign(response, remoteIdentity.PrivateKey, out var signature));
        response.Signature = signature;

        var localHash = ComputePublicKeyHash(localIdentity.PublicKey);
        await InvokePrivateTask(discoveryService, "HandleAuthResponseAsync", response, verifiedAddress,
            localIdentity.PublicKey, localHash, CancellationToken.None);

        var announcement = new DiscoveryInfo
        {
            MessageType = "announce",
            Version = "0.1",
            Id = remoteHash,
            TcpPort = 23001,
            TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        var unverifiedAddress = IPAddress.Parse("127.0.0.2");
        await InvokePrivateTask(discoveryService, "HandleAnnouncementAsync", announcement, unverifiedAddress,
            localHash, CancellationToken.None);

        Assert.AreEqual(0, GetPendingAuthRequestCount(discoveryService));
        Assert.AreEqual(verifiedAddress, discoveryService.Devices[0].Ipv4Address);
    }

    [TestMethod]
    public async Task Discovery_DuplicateAnnouncementsBeforeAuthentication_SendOneAuthRequest()
    {
        var localIdentity = CreateIdentity();
        using var discoveryService = new DeviceDiscoveryService(
            new FakeDeviceCommunicationSettings(),
            new FakeIdentityStore(localIdentity),
            new FakeLocalDataEndpointProvider(23001));
        var remoteIdentity = CreateIdentity();
        var announcement = new DiscoveryInfo
        {
            MessageType = "announce",
            Version = "0.1",
            Id = ComputePublicKeyHash(remoteIdentity.PublicKey),
            TcpPort = 23002,
            TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        var localHash = ComputePublicKeyHash(localIdentity.PublicKey);

        await InvokePrivateTask(discoveryService, "HandleAnnouncementAsync", announcement, IPAddress.Loopback,
            localHash, CancellationToken.None);
        await InvokePrivateTask(discoveryService, "HandleAnnouncementAsync", announcement, IPAddress.Loopback,
            localHash, CancellationToken.None);

        Assert.AreEqual(1, GetPendingAuthRequestCount(discoveryService));
    }

    [TestMethod]
    public async Task Discovery_SignedAnnouncement_PublishesDeviceWithoutAuthRequest()
    {
        var localIdentity = CreateIdentity();
        using var discoveryService = new DeviceDiscoveryService(
            new FakeDeviceCommunicationSettings(),
            new FakeIdentityStore(localIdentity),
            new FakeLocalDataEndpointProvider(23001));
        var remoteIdentity = CreateIdentity();
        var announcement = new DiscoveryInfo
        {
            MessageType = "announce",
            Version = "0.2",
            Id = ComputePublicKeyHash(remoteIdentity.PublicKey),
            Name = "peer",
            TcpPort = 23002,
            TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            PublicKey = remoteIdentity.PublicKey
        };
        Assert.IsTrue(DeviceDiscoverySignature.TrySign(announcement, remoteIdentity.PrivateKey, out var signature));
        announcement.Signature = signature;

        await InvokePrivateTask(discoveryService, "HandleAnnouncementAsync", announcement, IPAddress.Loopback,
            ComputePublicKeyHash(localIdentity.PublicKey), CancellationToken.None);

        Assert.AreEqual(1, discoveryService.Devices.Count);
        Assert.AreEqual(remoteIdentity.PublicKey, discoveryService.Devices[0].Id);
        Assert.AreEqual(0, GetPendingAuthRequestCount(discoveryService));
    }

    [TestMethod]
    public async Task Discovery_ResponseFromDifferentAddress_ConsumesChallengeByNonce()
    {
        var localIdentity = CreateIdentity();
        using var discoveryService = new DeviceDiscoveryService(
            new FakeDeviceCommunicationSettings(),
            new FakeIdentityStore(localIdentity),
            new FakeLocalDataEndpointProvider(23001));
        var remoteIdentity = CreateIdentity();
        var remoteHash = ComputePublicKeyHash(remoteIdentity.PublicKey);
        var requestedAddress = IPAddress.Parse("127.0.0.1");
        var responseAddress = IPAddress.Parse("127.0.0.2");
        const string requestedNonce = "requested-nonce";

        InvokePrivateVoid(discoveryService, "RegisterPendingAuthRequest", remoteHash, requestedNonce, requestedAddress);
        var localHash = ComputePublicKeyHash(localIdentity.PublicKey);
        var response = CreateSignedAuthResponse(remoteIdentity, remoteHash, requestedNonce, 23001);
        await InvokePrivateTask(discoveryService, "HandleAuthResponseAsync", response, responseAddress,
            localIdentity.PublicKey, localHash, CancellationToken.None);

        Assert.AreEqual(1, discoveryService.Devices.Count);
        Assert.AreEqual(responseAddress, discoveryService.Devices[0].Ipv4Address);
        Assert.AreEqual(0, GetPendingAuthRequestCount(discoveryService));

        var announcement = new DiscoveryInfo
        {
            MessageType = "announce",
            Version = "0.1",
            Id = remoteHash,
            TcpPort = 23001,
            TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        await InvokePrivateTask(discoveryService, "HandleAnnouncementAsync", announcement, requestedAddress,
            localHash, CancellationToken.None);

        Assert.AreEqual(0, GetPendingAuthRequestCount(discoveryService));
        Assert.AreEqual(1, GetVerifiedEndpointCount(discoveryService, remoteIdentity.PublicKey));
    }

    [TestMethod]
    public async Task Discovery_ResponseNonceMatchesPendingChallenge_UsesResponseAddress()
    {
        var localIdentity = CreateIdentity();
        using var discoveryService = new DeviceDiscoveryService(
            new FakeDeviceCommunicationSettings(),
            new FakeIdentityStore(localIdentity),
            new FakeLocalDataEndpointProvider(23001));
        var remoteIdentity = CreateIdentity();
        var remoteHash = ComputePublicKeyHash(remoteIdentity.PublicKey);
        var requestedAddress = IPAddress.Parse("127.0.0.1");
        var responseAddress = IPAddress.Parse("127.0.0.2");
        const string nonce = "pending-nonce";

        InvokePrivateVoid(discoveryService, "RegisterPendingAuthRequest", remoteHash, nonce, requestedAddress);

        var response = CreateSignedAuthResponse(remoteIdentity, remoteHash, nonce, 23001);
        var localHash = ComputePublicKeyHash(localIdentity.PublicKey);
        await InvokePrivateTask(discoveryService, "HandleAuthResponseAsync", response, responseAddress,
            localIdentity.PublicKey, localHash, CancellationToken.None);

        Assert.AreEqual(1, discoveryService.Devices.Count);
        Assert.AreEqual(responseAddress, discoveryService.Devices[0].Ipv4Address);
        Assert.AreEqual(0, GetPendingAuthRequestCount(discoveryService));
    }

    [TestMethod]
    public async Task Discovery_DualStackResponses_AuthenticateBothEndpoints()
    {
        var localIdentity = CreateIdentity();
        using var discoveryService = new DeviceDiscoveryService(
            new FakeDeviceCommunicationSettings(),
            new FakeIdentityStore(localIdentity),
            new FakeLocalDataEndpointProvider(23001));
        var remoteIdentity = CreateIdentity();
        var remoteHash = ComputePublicKeyHash(remoteIdentity.PublicKey);
        var ipv4Address = IPAddress.Parse("192.0.2.10");
        var ipv6Address = IPAddress.Parse("2001:db8::10");
        const string ipv4Nonce = "ipv4-nonce";
        const string ipv6Nonce = "ipv6-nonce";

        InvokePrivateVoid(discoveryService, "RegisterPendingAuthRequest", remoteHash, ipv4Nonce, ipv4Address);
        InvokePrivateVoid(discoveryService, "RegisterPendingAuthRequest", remoteHash, ipv6Nonce, ipv6Address);
        var localHash = ComputePublicKeyHash(localIdentity.PublicKey);

        await InvokePrivateTask(discoveryService, "HandleAuthResponseAsync",
            CreateSignedAuthResponse(remoteIdentity, remoteHash, ipv4Nonce, 23001), ipv4Address,
            localIdentity.PublicKey, localHash, CancellationToken.None);
        await InvokePrivateTask(discoveryService, "HandleAuthResponseAsync",
            CreateSignedAuthResponse(remoteIdentity, remoteHash, ipv6Nonce, 23001), ipv6Address,
            localIdentity.PublicKey, localHash, CancellationToken.None);

        Assert.AreEqual(1, discoveryService.Devices.Count);
        Assert.AreEqual(ipv4Address, discoveryService.Devices[0].Ipv4Address);
        Assert.AreEqual(ipv6Address, discoveryService.Devices[0].Ipv6Address);
        Assert.AreEqual(0, GetPendingAuthRequestCount(discoveryService));
        Assert.AreEqual(2, GetVerifiedEndpointCount(discoveryService, remoteIdentity.PublicKey));
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

    private static DiscoveryInfo CreateSignedAuthResponse(
        (string PublicKey, string PrivateKey) identity,
        string id,
        string nonce,
        int tcpPort)
    {
        var response = new DiscoveryInfo
        {
            MessageType = "auth.response",
            Version = "0.1",
            Id = id,
            TcpPort = tcpPort,
            TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            PublicKey = identity.PublicKey,
            Nonce = nonce
        };
        Assert.IsTrue(DeviceDiscoverySignature.TrySign(response, identity.PrivateKey, out var signature));
        response.Signature = signature;
        return response;
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

    private static int GetVerifiedEndpointCount(object instance, string deviceId)
    {
        var field = instance.GetType().GetField(
            "_verifiedEndpoints",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.IsNotNull(field, "Verified endpoint field not found.");
        var value = field.GetValue(instance) as System.Collections.IDictionary;
        Assert.IsNotNull(value, "Verified endpoint dictionary not found.");
        var endpoints = value[deviceId] as System.Collections.ICollection;
        Assert.IsNotNull(endpoints, "Verified endpoint list not found.");
        return endpoints.Count;
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

        public bool IsAvailable { get; init; } = true;

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
            return IsAvailable;
        }

        public DeviceIdentity EnsureIdentity()
        {
            return _identity;
        }
    }

    private sealed class DiscoveryLogSink(string expectedMessage) : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Events { get; } = new();
        public TaskCompletionSource<LogEvent> ExpectedEvent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Emit(LogEvent logEvent)
        {
            Events.Enqueue(logEvent);
            if (logEvent.RenderMessage().Contains(expectedMessage, StringComparison.Ordinal))
            {
                ExpectedEvent.TrySetResult(logEvent);
            }
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
