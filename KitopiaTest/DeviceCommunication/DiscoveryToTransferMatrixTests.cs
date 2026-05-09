using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using Core.Services.Config;
using Core.Services.DeviceCommunication;
using Core.Services.DeviceCommunication.Application;
using Core.Services.DeviceCommunication.Discovery;
using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Messages.Clipboard;
using Core.Services.DeviceCommunication.Routing;
using Core.Services.DeviceCommunication.Sessions;
using Core.ViewModel.Windows;
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
    [DataRow(true, true, true, true)]
    [DataRow(true, true, true, false)]
    [DataRow(true, true, false, true)]
    [DataRow(true, true, false, false)]
    [DataRow(true, false, true, true)]
    [DataRow(true, false, true, false)]
    [DataRow(true, false, false, true)]
    [DataRow(true, false, false, false)]
    [DataRow(false, true, true, true)]
    [DataRow(false, true, true, false)]
    [DataRow(false, true, false, true)]
    [DataRow(false, true, false, false)]
    [DataRow(false, false, true, true)]
    [DataRow(false, false, true, false)]
    [DataRow(false, false, false, true)]
    [DataRow(false, false, false, false)]
    public async Task DiscoveryToTransfer_CapabilityMatrix_CoversAllCombinations(
        bool localSupportsQuic,
        bool remoteSupportsQuic,
        bool localSupportsIpv6,
        bool remoteSupportsIpv6)
    {
        var localIdentity = CreateIdentity();
        ConfigManger.Config.devicePrivateKey = localIdentity.PrivateKey;
        ConfigManger.Config.EnsureDeviceIdentity();

        using var discoveryService = new DeviceDiscoveryService();
        var remoteDevice = BuildAuthenticatedRemoteDevice(discoveryService, localIdentity, remoteSupportsQuic, remoteSupportsIpv6);
        Assert.IsNotNull(remoteDevice);

        var messageApp = new CapabilityAwareMessageAppService(localSupportsQuic, localSupportsIpv6);
        using var viewModel = new LanFileShareWindowViewModel(discoveryService, new MatrixFakeLocalDataListener(), messageApp, new FakeToastService());

        await using var payload = new MemoryStream([1, 2, 3, 4], writable: false);
        var fileMessage = new FileChatMessage(remoteDevice.Id, Guid.NewGuid(), "demo.bin", 4);

        var sendTask = InvokeSendFileToDeviceAsync(viewModel, remoteDevice, fileMessage, payload);

        var expectedToFail = remoteSupportsIpv6 && !localSupportsIpv6;
        if (expectedToFail)
        {
            await Assert.ThrowsExactlyAsync<NotSupportedException>(() => sendTask);
        }
        else
        {
            await sendTask;
        }

        var expectedPrimaryProtocol = remoteSupportsQuic ? LocalDataTransportProtocol.Quic : LocalDataTransportProtocol.Tcp;
        Assert.AreEqual(expectedPrimaryProtocol, messageApp.Attempts[0].Protocol);

        var expectedAddressFamily = remoteSupportsIpv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
        Assert.AreEqual(expectedAddressFamily, messageApp.Attempts[0].RemoteEndPoint.AddressFamily);

        var firstAttemptShouldFail =
            (remoteSupportsQuic && !localSupportsQuic) ||
            (remoteSupportsIpv6 && !localSupportsIpv6);

        if (remoteSupportsQuic && firstAttemptShouldFail)
        {
            Assert.AreEqual(2, messageApp.Attempts.Count);
            Assert.AreEqual(LocalDataTransportProtocol.Tcp, messageApp.Attempts[1].Protocol);
        }
        else
        {
            Assert.AreEqual(1, messageApp.Attempts.Count);
        }
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

        InvokePrivateVoid(
            discoveryService,
            "RegisterPendingAuthRequest",
            remoteHash,
            nonce,
            remoteAddress);

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

    [TestMethod]
    public async Task DiscoveryToTransfer_QuicAlpnNegotiationError_FallsBackToTcp()
    {
        var localIdentity = CreateIdentity();
        ConfigManger.Config.devicePrivateKey = localIdentity.PrivateKey;
        ConfigManger.Config.EnsureDeviceIdentity();

        using var discoveryService = new DeviceDiscoveryService();
        var remoteDevice = BuildAuthenticatedRemoteDevice(
            discoveryService,
            localIdentity,
            supportsQuic: true,
            supportsIpv6: true);

        var messageApp = new CapabilityAwareMessageAppService(
            localSupportsQuic: true,
            localSupportsIpv6: true,
            failFirstQuicAttemptWithAuthError: true);
        using var viewModel = new LanFileShareWindowViewModel(discoveryService, new MatrixFakeLocalDataListener(), messageApp, new FakeToastService());

        await using var payload = new MemoryStream([9, 8, 7], writable: false);
        var fileMessage = new FileChatMessage(remoteDevice.Id, Guid.NewGuid(), "alpn.bin", 3);

        await InvokeSendFileToDeviceAsync(viewModel, remoteDevice, fileMessage, payload);

        Assert.AreEqual(2, messageApp.Attempts.Count);
        Assert.AreEqual(LocalDataTransportProtocol.Quic, messageApp.Attempts[0].Protocol);
        Assert.AreEqual(LocalDataTransportProtocol.Tcp, messageApp.Attempts[1].Protocol);
        Assert.AreEqual(AddressFamily.InterNetworkV6, messageApp.Attempts[0].RemoteEndPoint.AddressFamily);
        Assert.AreEqual(AddressFamily.InterNetworkV6, messageApp.Attempts[1].RemoteEndPoint.AddressFamily);
    }

    private static DeviceModel BuildAuthenticatedRemoteDevice(
        DeviceDiscoveryService discoveryService,
        (string PublicKey, string PrivateKey) localIdentity,
        bool supportsQuic,
        bool supportsIpv6)
    {
        var remoteIdentity = CreateIdentity();
        var remoteHash = ComputePublicKeyHash(remoteIdentity.PublicKey);
        var nonce = Guid.NewGuid().ToString("N");
        var remoteAddress = supportsIpv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;

        InvokePrivateVoid(
            discoveryService,
            "RegisterPendingAuthRequest",
            remoteHash,
            nonce,
            remoteAddress);

        var response = new DiscoveryInfo
        {
            MessageType = "auth.response",
            Version = "0.1",
            Id = remoteHash,
            Name = "peer-ok",
            TcpPort = 22001,
            SupportsQuic = supportsQuic,
            QuicPort = supportsQuic ? 22002 : 0,
            TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            PublicKey = remoteIdentity.PublicKey,
            Nonce = nonce
        };

        response.Signature = Convert.ToBase64String(SignDiscoveryInfo(response, remoteIdentity.PrivateKey));

        var localHash = ComputePublicKeyHash(localIdentity.PublicKey);
        InvokePrivateVoid(discoveryService, "HandleAuthResponse", response, remoteAddress, localIdentity.PublicKey, localHash);

        Assert.AreEqual(1, discoveryService.Devices.Count);
        var device = discoveryService.Devices[0];
        Assert.AreEqual(remoteIdentity.PublicKey, device.Id);
        Assert.AreEqual(supportsQuic, device.SupportQuic);
        Assert.AreEqual(22001, device.TcpPort);
        Assert.AreEqual(supportsQuic ? 22002 : 0, device.QuicPort);
        return device;
    }

    private static Task InvokeSendFileToDeviceAsync(
        LanFileShareWindowViewModel viewModel,
        DeviceModel device,
        FileChatMessage message,
        Stream stream)
    {
        var method = typeof(LanFileShareWindowViewModel).GetMethod(
            "SendFileToDeviceAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.IsNotNull(method);
        var result = method.Invoke(viewModel, [device, message, stream]);
        return result as Task ?? throw new InvalidOperationException("SendFileToDeviceAsync did not return Task.");
    }

    private static byte[] SignDiscoveryInfo(DiscoveryInfo info, string privateKey)
    {
        var payload = BuildDiscoveryPayload(info);
        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKey), out _);
        return rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    private static byte[] BuildDiscoveryPayload(DiscoveryInfo info)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(info.Id ?? string.Empty);
        writer.Write(info.Name ?? string.Empty);
        writer.Write(info.TcpPort);
        writer.Write(info.QuicPort);
        writer.Write(info.SupportsQuic);
        writer.Write(info.TimestampUnixSeconds);
        writer.Write(info.Nonce ?? string.Empty);
        writer.Flush();
        return stream.ToArray();
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

    private sealed class CapabilityAwareMessageAppService : IMessageAppService
    {
        private readonly bool _localSupportsQuic;
        private readonly bool _localSupportsIpv6;
        private readonly bool _failFirstQuicAttemptWithAuthError;
        private bool _quicAttemptFailed;

        public CapabilityAwareMessageAppService(
            bool localSupportsQuic,
            bool localSupportsIpv6,
            bool failFirstQuicAttemptWithAuthError = false)
        {
            _localSupportsQuic = localSupportsQuic;
            _localSupportsIpv6 = localSupportsIpv6;
            _failFirstQuicAttemptWithAuthError = failFirstQuicAttemptWithAuthError;
        }

        public List<MessageContext> Attempts { get; } = [];

        public ValueTask SendTextChatAsync(MessageContext context, TextChatMessage message,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask SendFileChatAsync(MessageContext context, FileChatMessage message, Stream stream,
            CancellationToken cancellationToken = default)
        {
            Attempts.Add(context);

            if (context.Protocol == LocalDataTransportProtocol.Quic &&
                _failFirstQuicAttemptWithAuthError &&
                !_quicAttemptFailed)
            {
                _quicAttemptFailed = true;
                throw new AuthenticationException("Application layer protocol negotiation error was encountered.");
            }

            if (context.Protocol == LocalDataTransportProtocol.Quic && !_localSupportsQuic)
            {
                throw new NotSupportedException("local_quic_not_supported");
            }

            if (context.RemoteEndPoint.AddressFamily == AddressFamily.InterNetworkV6 && !_localSupportsIpv6)
            {
                throw new NotSupportedException("local_ipv6_not_supported");
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask SendImageChatAsync(MessageContext context, ImageChatMessage message, Stream stream,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask AcceptFileAsync(MessageContext context, Guid transferId, string savePath,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask RejectFileAsync(MessageContext context, Guid transferId, string reason,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask CancelTransferAsync(MessageContext context, Guid transferId, string reason,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask SendClipboardTextAsync(MessageContext context, TextClipboardMessage message,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public IAsyncEnumerable<IncomingMessageEvent> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            return AsyncEnumerable.Empty<IncomingMessageEvent>();
        }

        public void UpdateDisplayContext(bool isMainWindowActive, bool isDeviceChatPageOpen, string? selectedConversationId)
        {
        }

        public void RequestOpenConversation(string conversationId)
        {
        }

        public string? GetRequestedConversationId()
        {
            return null;
        }

        public void ClearRequestedConversationId()
        {
        }

        public IncomingMessageDisplayMode ResolveIncomingDisplayMode(string conversationId)
        {
            return IncomingMessageDisplayMode.NotifyByToast;
        }

        public IncomingMessageDisplayMode ResolveIncomingDisplayMode(bool isMainWindowActive, bool isDeviceChatPageOpen,
            string conversationId, string? selectedConversationId)
        {
            return IncomingMessageDisplayMode.NotifyByToast;
        }
    }

    private sealed class FakeToastService : IToastService
    {
        public void Init()
        {
        }

        public Task Show(string header, string text,
            Avalonia.Controls.Notifications.NotificationType notificationType = Avalonia.Controls.Notifications.NotificationType.Information,
            Avalonia.Controls.Window? dialogWindow = null)
        {
            return Task.CompletedTask;
        }

        public Task Show(ToastRequest request, Avalonia.Controls.Window? dialogWindow = null)
        {
            return Task.CompletedTask;
        }

        public IToastProgressHandle ShowProgress(string header, string text,
            Avalonia.Controls.Notifications.NotificationType notificationType = Avalonia.Controls.Notifications.NotificationType.Information,
            double initialProgress = 0, bool isIndeterminate = false)
        {
            throw new NotSupportedException();
        }

        public void Unregister()
        {
        }

        public bool HasUnreadSuppressedNotifications() => false;

        public bool TryOpenLatestSuppressedNotification() => false;

        public bool ShowSuppressedNotificationCenter() => false;

        public void ClearUnreadSuppressedNotifications()
        {
        }
    }

    private sealed class MatrixFakeLocalDataListener : ILocalDataListener
    {
        public int TcpPort => 12345;
        public int QuicPort => 12346;
        public bool SupportsQuic => true;

        public Task StartListeningAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task StopListeningAsync() => Task.CompletedTask;

        public Task SendAsync(LocalDataTransportProtocol protocol, ReadOnlyMemory<byte> payload, System.Net.IPEndPoint remoteEndPoint,
            string? remoteIdentityPublicKey = null, CancellationToken token = default) => Task.CompletedTask;

        public Task SendAsync(LocalDataTransportProtocol protocol, System.IO.Pipelines.PipeReader payloadReader, System.Net.IPEndPoint remoteEndPoint,
            string? remoteIdentityPublicKey = null, CancellationToken token = default) => Task.CompletedTask;

        public Task SendAsync(LocalDataTransportProtocol protocol, Stream stream, System.Net.IPEndPoint remoteEndPoint,
            string? remoteIdentityPublicKey = null, CancellationToken token = default) => Task.CompletedTask;
    }
}
