using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Core.Services.Config;
using Microsoft.Extensions.DependencyInjection;
using ObservableCollections;
using PluginCore;
using Serilog;

namespace Core.Services.DeviceCommunication.Discovery;

public sealed class DeviceDiscoveryService : IDeviceDiscoveryService {
    private static readonly ILogger Logger = LogManager.Logger.ForContext<IDeviceDiscoveryService>();
    private const int DiscoveryPort = 53535;
    private const string MulticastAddressV4 = "239.255.255.250";
    private const string MulticastAddressV6 = "ff02::1";
    private const int DiscoveryIpv4Ttl = 1;
    private const long DiscoverySignatureToleranceSeconds = 60;
    private const string DiscoveryMessageTypeAnnounce = "announce";
    private const string DiscoveryMessageTypeAuthRequest = "auth.request";
    private const string DiscoveryMessageTypeAuthResponse = "auth.response";
    private static readonly TimeSpan DiscoveryBroadcastInterval = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan DiscoveryCleanupInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DiscoveryListenerRefreshInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DiscoveryStaleTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DiscoveryAuthRequestTimeout = TimeSpan.FromSeconds(15);

    private readonly object _sync = new();
    private readonly ObservableList<DeviceModel> _devicesSource = [];
    private readonly ISynchronizedView<DeviceModel, DeviceModel> _devicesView;
    private readonly Dictionary<string, PendingAuthRequest> _pendingAuthRequests = new(StringComparer.Ordinal);

    private CancellationTokenSource? _cts;
    private UdpClient? _udpClientV4;
    private UdpClient? _udpClientV6;

    private readonly record struct PendingAuthRequest(string Nonce, DateTime ExpiresAtUtc);

    public NotifyCollectionChangedSynchronizedViewList<DeviceModel> Devices { get; }
    

    public DeviceDiscoveryService() {
        _devicesView = _devicesSource.CreateView(device => device);
        Devices = _devicesView.ToNotifyCollectionChanged();
    }

    public async Task StartAsync(CancellationToken token1) {
        lock (_sync) {
            StopCore();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(() => DiscoveryLoop(token), token);
            _ = Task.Run(() => BroadcastLoop(token), token);
            _ = Task.Run(() => CleanupLoop(token), token);
        }
    }

    public async Task StopAsync() {
        lock (_sync) {
            StopCore();
        }
    }

    private void StopCore() {
        var cts = _cts;
        _cts = null;
        if (cts is not null) {
            try {
                cts.Cancel();
            }
            catch { }

            cts.Dispose();
        }

        CloseUdpClient(ref _udpClientV4);
        CloseUdpClient(ref _udpClientV6);
        _pendingAuthRequests.Clear();
        _devicesSource.Clear();
    }

    private static void CloseUdpClient(ref UdpClient? client) {
        if (client is null) {
            return;
        }

        try {
            client.Close();
        }
        catch { }
        finally {
            client = null;
        }
    }

    private async Task CleanupLoop(CancellationToken token) {
        while (!token.IsCancellationRequested) {
            try {
                await Task.Delay(DiscoveryCleanupInterval, token);
            }
            catch (OperationCanceledException) {
                break;
            }
            lock (_sync) {
                var now = DateTime.UtcNow;
                var staleDevices = _devicesSource
                    .Where(device => now - device.LastSeen > DiscoveryStaleTimeout)
                    .ToList();
                foreach (var staleDevice in staleDevices) {
                    _devicesSource.Remove(staleDevice);
                }
            }
            
        }
    }

    private async Task DiscoveryLoop(CancellationToken token) {
        _udpClientV4 = new UdpClient(AddressFamily.InterNetwork);
        _udpClientV4.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udpClientV4.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

        try {
            _udpClientV6 = new UdpClient(AddressFamily.InterNetworkV6);
            _udpClientV6.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpClientV6.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
            _udpClientV6.Client.Bind(new IPEndPoint(IPAddress.IPv6Any, DiscoveryPort));
        }
        catch {
            _udpClientV6 = null;
        }

        var multicastIpV4 = IPAddress.Parse(MulticastAddressV4);
        var multicastIpV6 = IPAddress.Parse(MulticastAddressV6);

        try {
            _udpClientV4.MulticastLoopback = true;
            _udpClientV6?.MulticastLoopback = true;
            _udpClientV4.JoinMulticastGroup(multicastIpV4);
            _udpClientV6?.JoinMulticastGroup(multicastIpV6);
            RefreshDiscoveryMulticastMembership(_udpClientV4, _udpClientV6, multicastIpV4, multicastIpV6);
        }
        catch (Exception e) {
            Logger.Error(e, "加入组播组失");
        }

        var receiveTasks = new List<Task> { ReceiveLoop(_udpClientV4, token) };
        if (_udpClientV6 != null) {
            receiveTasks.Add(ReceiveLoop(_udpClientV6, token));
        }

        receiveTasks.Add(RefreshDiscoveryMulticastMembershipLoop(_udpClientV4, _udpClientV6, multicastIpV4, multicastIpV6, token));

        await Task.WhenAll(receiveTasks);
    }

    private async Task RefreshDiscoveryMulticastMembershipLoop(UdpClient udpClientV4, UdpClient? udpClientV6,
        IPAddress multicastIpV4, IPAddress multicastIpV6, CancellationToken token) {
        while (!token.IsCancellationRequested) {
            try {
                await Task.Delay(DiscoveryListenerRefreshInterval, token);
            }
            catch (OperationCanceledException) {
                break;
            }

            RefreshDiscoveryMulticastMembership(udpClientV4, udpClientV6, multicastIpV4, multicastIpV6);
        }
    }

    private void RefreshDiscoveryMulticastMembership(UdpClient udpClientV4, UdpClient? udpClientV6,
        IPAddress multicastIpV4, IPAddress multicastIpV6) {
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces()) {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                !networkInterface.SupportsMulticast ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback) {
                continue;
            }

            var props = networkInterface.GetIPProperties();
            foreach (var unicast in props.UnicastAddresses) {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork) {
                    try {
                        udpClientV4.JoinMulticastGroup(multicastIpV4, unicast.Address);
                    }
                    catch { }
                }
            }

            if (udpClientV6 is null) {
                continue;
            }

            try {
                var ipv6Properties = props.GetIPv6Properties();
                if (ipv6Properties?.Index <= 0) {
                    continue;
                }

                udpClientV6.JoinMulticastGroup(ipv6Properties.Index, multicastIpV6);
            }
            catch { }
        }
    }

    private async Task ReceiveLoop(UdpClient client, CancellationToken token) {
        while (!token.IsCancellationRequested) {
            try {
                var result = await client.ReceiveAsync(token);
                var info = JsonSerializer.Deserialize<DiscoveryInfo>(Encoding.UTF8.GetString(result.Buffer));
                if (info is null) {
                    continue;
                }

                if (!TryGetLocalIdentity(out var localPublicKey, out var localIdHash)) {
                    continue;
                }

                var endpointAddress = NormalizeAddress(result.RemoteEndPoint.Address);
                var messageType = NormalizeMessageType(info.MessageType);

                switch (messageType) {
                    case DiscoveryMessageTypeAnnounce:
                        await HandleAnnouncementAsync(info, endpointAddress, localIdHash, token);
                        break;
                    case DiscoveryMessageTypeAuthRequest:
                        await HandleAuthRequestAsync(info, endpointAddress, localIdHash, localPublicKey, token);
                        break;
                    case DiscoveryMessageTypeAuthResponse:
                        HandleAuthResponse(info, endpointAddress, localPublicKey, localIdHash);
                        break;
                }
            }
            catch (OperationCanceledException) {
                break;
            }
            catch { }
        }
    }

    private async Task BroadcastLoop(CancellationToken token) {
        var multicastIpV4 = IPAddress.Parse(MulticastAddressV4);
        var multicastIpV6 = IPAddress.Parse(MulticastAddressV6);
        var multicastEndpointV4 = new IPEndPoint(multicastIpV4, DiscoveryPort);

        while (!token.IsCancellationRequested) {
            try {
                await Task.Delay(DiscoveryBroadcastInterval, token);
                var localDataListener = ServiceManager.Services.GetService<ILocalDataListener>()!;
                var tcpPort = localDataListener.TcpPort;
                if (tcpPort <= 0) {
                    continue;
                }

                if (!TryGetLocalIdentity(out _, out var localIdHash)) {
                    continue;
                }

                var info = new DiscoveryInfo {
                    MessageType = DiscoveryMessageTypeAnnounce,
                    Id = localIdHash,
                    Name = string.IsNullOrWhiteSpace(ConfigManger.Config.deviceBroadcastName)? Environment.MachineName : ConfigManger.Config.deviceBroadcastName.Trim(),
                    TcpPort = tcpPort,
                    QuicPort = localDataListener.QuicPort,
                    SupportsQuic = localDataListener.SupportsQuic,
                    TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(info));

                foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces()) {
                    if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                        !networkInterface.SupportsMulticast ||
                        networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback) {
                        continue;
                    }

                    var props = networkInterface.GetIPProperties();
                    foreach (var unicast in props.UnicastAddresses) {
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork) {
                            try {
                                using var client = new UdpClient();
                                client.Client.Bind(new IPEndPoint(unicast.Address, 0));
                                client.Ttl = DiscoveryIpv4Ttl;
                                await client.SendAsync(bytes, bytes.Length, multicastEndpointV4);
                            }
                            catch (Exception) { }
                        }
                        else if (unicast.Address.AddressFamily == AddressFamily.InterNetworkV6) {
                            try {
                                using var client = new UdpClient(AddressFamily.InterNetworkV6);
                                client.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
                                client.Client.Bind(new IPEndPoint(unicast.Address, 0));
                                var multicastAddressV6WithScope =
                                    new IPAddress(multicastIpV6.GetAddressBytes(), unicast.Address.ScopeId);
                                var multicastEndpointV6 = new IPEndPoint(multicastAddressV6WithScope, DiscoveryPort);
                                await client.SendAsync(bytes, bytes.Length, multicastEndpointV6);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (OperationCanceledException) {
                break;
            }
            catch { }

            try {
                await Task.Delay(DiscoveryBroadcastInterval, token);
            }
            catch (OperationCanceledException) {
                break;
            }
        }
    }

    private async Task HandleAnnouncementAsync(DiscoveryInfo info, IPAddress endpointAddress, string localIdHash,
        CancellationToken token) {
        if (string.IsNullOrWhiteSpace(info.Id) || info.TcpPort <= 0 ||
            info is { SupportsQuic: true, QuicPort: <= 0 } ||
            string.Equals(info.Id, localIdHash, StringComparison.Ordinal)) {
            return;
        }

        var nonce = CreateNonce();
        RegisterPendingAuthRequest(info.Id, nonce, endpointAddress);

        var request = new DiscoveryInfo {
            MessageType = DiscoveryMessageTypeAuthRequest,
            Id = info.Id,
            TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Nonce = nonce
        };

        await SendUnicastAsync(request, new IPEndPoint(endpointAddress, DiscoveryPort), token);
    }

    private async Task HandleAuthRequestAsync(DiscoveryInfo info, IPAddress endpointAddress, string localIdHash,
        string localPublicKey, CancellationToken token) {
        if (!string.Equals(info.Id, localIdHash, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(info.Nonce)) {
            return;
        }

        var localDataListener = ServiceManager.Services.GetService<ILocalDataListener>();
        if (localDataListener is null || localDataListener.TcpPort <= 0) {
            return;
        }

        var response = new DiscoveryInfo {
            MessageType = DiscoveryMessageTypeAuthResponse,
            Id = localIdHash,
            Name = string.IsNullOrWhiteSpace(ConfigManger.Config.deviceBroadcastName)
                ? Environment.MachineName
                : ConfigManger.Config.deviceBroadcastName.Trim(),
            TcpPort = localDataListener.TcpPort,
            QuicPort = localDataListener.QuicPort,
            SupportsQuic = localDataListener.SupportsQuic,
            TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            PublicKey = localPublicKey,
            Nonce = info.Nonce
        };

        if (!DeviceDiscoverySignature.TrySign(response, ConfigManger.Config.devicePrivateKey, out var signature)) {
            return;
        }

        response.Signature = signature;
        await SendUnicastAsync(response, new IPEndPoint(endpointAddress, DiscoveryPort), token);
    }

    private void HandleAuthResponse(DiscoveryInfo info, IPAddress endpointAddress, string localPublicKey,
        string localIdHash) {
        if (string.IsNullOrWhiteSpace(info.Id) ||
            string.IsNullOrWhiteSpace(info.PublicKey) ||
            string.IsNullOrWhiteSpace(info.Signature) ||
            info.TcpPort <= 0 ||
            info is { SupportsQuic: true, QuicPort: <= 0 } ||
            string.Equals(info.PublicKey, localPublicKey, StringComparison.Ordinal) ||
            string.Equals(info.Id, localIdHash, StringComparison.Ordinal)) {
            return;
        }

        if (!TryTakePendingAuthNonce(info.Id, endpointAddress, out var expectedNonce) ||
            !IsAuthenticated(info, expectedNonce)) {
            return;
        }

        UpsertAuthenticatedDevice(info, endpointAddress);
    }

    private void UpsertAuthenticatedDevice(DiscoveryInfo info, IPAddress endpointAddress) {
        lock (_sync) {
            CleanupPendingAuthRequests(DateTime.UtcNow);

            var existing = _devicesSource.FirstOrDefault(device =>
                string.Equals(device.Id, info.PublicKey, StringComparison.Ordinal));
            if (existing is null) {
                var duplicateEndpoint = _devicesSource.FirstOrDefault(device =>
                    IsSameEndpoint(device, endpointAddress) &&
                    device.TcpPort == info.TcpPort &&
                    device.SupportQuic == info.SupportsQuic);
                if (duplicateEndpoint is not null) {
                    _devicesSource.Remove(duplicateEndpoint);
                }

                existing = new DeviceModel {
                    Id = info.PublicKey,
                    Name = string.IsNullOrWhiteSpace(info.Name) ? "未知设备" : info.Name.Trim(),
                    CustomName = ConfigManger.Config.deviceCustomNames.TryGetValue(info.PublicKey, out var customName)
                        ? customName
                        : string.Empty,
                    TcpPort = info.TcpPort,
                    QuicPort = info.QuicPort,
                    SupportQuic = info.SupportsQuic,
                    LastSeen = DateTime.UtcNow
                };
                AssignDiscoveredAddress(existing, endpointAddress);
                _devicesSource.Add(existing);
            }
            else {
                existing.LastSeen = DateTime.UtcNow;
                existing.Name = string.IsNullOrWhiteSpace(info.Name) ? "未知设备" : info.Name.Trim();
                AssignDiscoveredAddress(existing, endpointAddress);

                existing.TcpPort = info.TcpPort;
                existing.QuicPort = info.QuicPort;
                existing.SupportQuic = info.SupportsQuic;
            }
        }
    }

    private bool TryTakePendingAuthNonce(string id, IPAddress endpointAddress, out string nonce) {
        lock (_sync) {
            CleanupPendingAuthRequests(DateTime.UtcNow);
            var requestKey = BuildPendingAuthKey(id, endpointAddress);
            if (!_pendingAuthRequests.TryGetValue(requestKey, out var pending)) {
                nonce = string.Empty;
                return false;
            }

            _pendingAuthRequests.Remove(requestKey);
            nonce = pending.Nonce;
            return true;
        }
    }

    private void RegisterPendingAuthRequest(string id, string nonce, IPAddress endpointAddress) {
        lock (_sync) {
            CleanupPendingAuthRequests(DateTime.UtcNow);
            var requestKey = BuildPendingAuthKey(id, endpointAddress);
            _pendingAuthRequests[requestKey] = new PendingAuthRequest(nonce, DateTime.UtcNow + DiscoveryAuthRequestTimeout);
        }
    }

    private static string BuildPendingAuthKey(string id, IPAddress endpointAddress) {
        return string.Create(id.Length + endpointAddress.ToString().Length + 1, (id, endpointAddress),
            (buffer, state) => {
                state.id.AsSpan().CopyTo(buffer);
                buffer[state.id.Length] = '|';
                state.endpointAddress.ToString().AsSpan().CopyTo(buffer[(state.id.Length + 1)..]);
            });
    }

    private void CleanupPendingAuthRequests(DateTime nowUtc) {
        if (_pendingAuthRequests.Count == 0) {
            return;
        }

        var expiredKeys = _pendingAuthRequests
            .Where(pair => pair.Value.ExpiresAtUtc <= nowUtc)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var key in expiredKeys) {
            _pendingAuthRequests.Remove(key);
        }
    }

    private static string NormalizeMessageType(string? messageType) {
        if (string.IsNullOrWhiteSpace(messageType)) {
            return DiscoveryMessageTypeAnnounce;
        }

        return messageType.Trim().ToLowerInvariant();
    }

    private static string CreateNonce() {
        return Guid.NewGuid().ToString("N");
    }

    private static bool IsAuthenticated(DiscoveryInfo info, string expectedNonce) {
        if (string.IsNullOrWhiteSpace(info.Signature) ||
            string.IsNullOrWhiteSpace(info.PublicKey) ||
            string.IsNullOrWhiteSpace(info.Id) ||
            !string.Equals(info.Nonce, expectedNonce, StringComparison.Ordinal)) {
            return false;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var skew = now >= info.TimestampUnixSeconds
            ? now - info.TimestampUnixSeconds
            : info.TimestampUnixSeconds - now;

        if (skew > DiscoverySignatureToleranceSeconds) {
            return false;
        }

        return DeviceDiscoverySignature.Verify(info);
    }

    private static bool TryGetLocalIdentity(out string publicKey, out string idHash) {
        publicKey = string.Empty;
        idHash = string.Empty;

        if (!DeviceDiscoverySignature.TryDerivePublicKey(ConfigManger.Config.devicePrivateKey, out publicKey)) {
            return false;
        }

        idHash = DeviceDiscoverySignature.ComputePublicKeyHash(publicKey);
        return !string.IsNullOrWhiteSpace(idHash);
    }

    private static async Task SendUnicastAsync(DiscoveryInfo info, IPEndPoint remoteEndPoint, CancellationToken token) {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(info));
        using var client = remoteEndPoint.AddressFamily == AddressFamily.InterNetworkV6
            ? new UdpClient(AddressFamily.InterNetworkV6)
            : new UdpClient(AddressFamily.InterNetwork);
        await client.SendAsync(bytes, bytes.Length, remoteEndPoint).WaitAsync(token);
    }

    private static bool IsSameEndpoint(DeviceModel device, IPAddress endpointAddress) {
        return device.Ipv4Address.Equals(endpointAddress) ||
               device.Ipv6Address.Equals(endpointAddress);
    }

    private static void AssignDiscoveredAddress(DeviceModel device, IPAddress endpointAddress) {
        if (endpointAddress.AddressFamily == AddressFamily.InterNetwork) {
            device.Ipv4Address = endpointAddress;
        }
        else if (endpointAddress.AddressFamily == AddressFamily.InterNetworkV6) {
            device.Ipv6Address = endpointAddress;
        }
    }
    
    private static IPAddress NormalizeAddress(IPAddress address) {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    public void Dispose() {
        StopAsync().GetAwaiter().GetResult();
        Devices.Dispose();
        _devicesView.Dispose();
    }
}


