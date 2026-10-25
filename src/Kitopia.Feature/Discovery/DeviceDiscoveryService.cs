using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Kitopia.Feature.DeviceCommunication.Serialization;
using Kitopia.Feature.DeviceCommunication.Diagnostics;
using Kitopia.Feature.DeviceCommunication.Identity;
using Kitopia.Feature.DeviceCommunication.Transport;
using ObservableCollections;

namespace Kitopia.Feature.DeviceCommunication.Discovery;

public sealed class DeviceDiscoveryService : IDeviceDiscoveryService
{
    private const string LogCategory = "DiscoveryService";
    private const int DiscoveryPort = 49526;
    private const string MulticastAddressV4 = "239.255.255.250";
    private const string MulticastAddressV6 = "ff02::1";
    private const int DiscoveryMulticastHopLimit = 1;
    private const long DiscoverySignatureToleranceSeconds = 60;
    private const string DiscoveryMessageTypeAnnounce = "announce";
    private const string DiscoveryMessageTypeAuthRequest = "auth.request";
    private const string DiscoveryMessageTypeAuthResponse = "auth.response";
    private const string DiscoveryProtocolVersion = "0.2";
    private static readonly Version DiscoveryProtocolVersionValue = Version.Parse(DiscoveryProtocolVersion);
    private readonly TimeSpan _discoveryBroadcastInterval = TimeSpan.FromSeconds(8);
    private readonly TimeSpan _discoveryCleanupInterval = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _discoveryListenerRefreshInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _discoveryStaleTimeoutBase = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _discoveryAuthRequestTimeoutBase = TimeSpan.FromSeconds(15);

    private readonly object _sync = new();
    private double _intervalMultiplier = 1.0;

    public void SetBackgroundMode(bool background)
    {
        _intervalMultiplier = background ? 3.0 : 1.0;
    }
    private readonly ObservableList<DiscoveredDevice> _devicesSource = [];
    private readonly ISynchronizedView<DiscoveredDevice, DiscoveredDevice> _devicesView;
    private readonly Dictionary<string, List<PendingAuthRequest>> _pendingAuthRequests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<VerifiedEndpoint>> _verifiedEndpoints = new(StringComparer.Ordinal);
    private readonly IDeviceCommunicationSettings _settings;
    private readonly IDeviceIdentityStore _identityStore;
    private readonly ILocalDataEndpointProvider _localDataEndpointProvider;

    private CancellationTokenSource? _cts;
    private UdpClient? _udpClientV4;
    private UdpClient? _udpClientV6;

    private readonly record struct PendingAuthRequest(
        string Nonce,
        DateTime ExpiresAtUtc,
        IPAddress RequestedAddress);
    private sealed class VerifiedEndpoint(IPAddress address, DateTime lastSeenUtc)
    {
        public IPAddress Address { get; } = address;
        public DateTime LastSeenUtc { get; set; } = lastSeenUtc;
    }

    public DeviceDiscoveryService(
        IDeviceCommunicationSettings settings,
        IDeviceIdentityStore identityStore,
        ILocalDataEndpointProvider localDataEndpointProvider)
    {
        _settings = settings;
        _identityStore = identityStore;
        _localDataEndpointProvider = localDataEndpointProvider;
        _devicesView = _devicesSource.CreateView(device => device);
        Devices = _devicesView.ToNotifyCollectionChanged();
    }

    public NotifyCollectionChangedSynchronizedViewList<DiscoveredDevice> Devices { get; }

    public Task StartAsync(CancellationToken token)
    {
        lock (_sync)
        {
            StopCore();
            DeviceCommunicationDiagnostics.Info(LogCategory, "Starting discovery service.");
            _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            var linkedToken = _cts.Token;
            _ = Task.Run(() => DiscoveryLoopAsync(linkedToken), linkedToken);
            _ = Task.Run(() => BroadcastLoopAsync(linkedToken), linkedToken);
            _ = Task.Run(() => CleanupLoopAsync(linkedToken), linkedToken);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        lock (_sync)
        {
            DeviceCommunicationDiagnostics.Info(LogCategory, "Stopping discovery service.");
            StopCore();
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        Devices.Dispose();
        _devicesView.Dispose();
    }

    private void StopCore()
    {
        var cts = _cts;
        _cts = null;
        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch
            {
            }

            cts.Dispose();
        }

        CloseUdpClient(ref _udpClientV4);
        CloseUdpClient(ref _udpClientV6);
        _pendingAuthRequests.Clear();
        _verifiedEndpoints.Clear();
        _devicesSource.Clear();
    }

    private static void CloseUdpClient(ref UdpClient? client)
    {
        if (client is null)
        {
            return;
        }

        try
        {
            client.Close();
        }
        catch (Exception exception)
        {
            DeviceCommunicationDiagnostics.Warning(LogCategory, $"CloseUdpClient failed: {exception.Message}");
        }
        finally
        {
            client = null;
        }
    }

    private TimeSpan Scaled(TimeSpan baseValue)
    {
        return _intervalMultiplier <= 1.0 ? baseValue : TimeSpan.FromMilliseconds(baseValue.TotalMilliseconds * _intervalMultiplier);
    }

    private async Task CleanupLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Scaled(_discoveryCleanupInterval), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            lock (_sync)
            {
                var now = DateTime.UtcNow;
                CleanupVerifiedEndpoints(now);
                var staleDevices = _devicesSource
                    .Where(device => now - device.LastSeen > Scaled(_discoveryStaleTimeoutBase))
                    .ToArray();
                foreach (var staleDevice in staleDevices)
                {
                    DeviceCommunicationDiagnostics.Info(
                        LogCategory,
                        $"Removing stale device {ShortId(staleDevice.Id)} at {FormatEndpoint(staleDevice)}.");
                    _verifiedEndpoints.Remove(staleDevice.Id);
                    _devicesSource.Remove(staleDevice);
                }
            }
        }
    }

    private async Task DiscoveryLoopAsync(CancellationToken token)
    {
        try
        {
            _udpClientV4 = new UdpClient(AddressFamily.InterNetwork);
            _udpClientV4.ExclusiveAddressUse = true;
            _udpClientV4.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            DeviceCommunicationDiagnostics.Info(LogCategory, $"Bound IPv4 discovery socket on {DiscoveryPort}.");
        }
        catch (Exception exception)
        {
            DeviceCommunicationDiagnostics.Error(LogCategory, "Failed to bind IPv4 discovery socket.", exception);
            throw;
        }

        try
        {
            _udpClientV6 = new UdpClient(AddressFamily.InterNetworkV6);
            _udpClientV6.ExclusiveAddressUse = true;
            _udpClientV6.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
            _udpClientV6.Client.Bind(new IPEndPoint(IPAddress.IPv6Any, DiscoveryPort));
            DeviceCommunicationDiagnostics.Info(LogCategory, $"Bound IPv6 discovery socket on {DiscoveryPort}.");
        }
        catch (Exception exception)
        {
            _udpClientV6 = null;
            DeviceCommunicationDiagnostics.Warning(LogCategory, $"IPv6 discovery socket unavailable: {exception.Message}");
        }

        var multicastIpV4 = IPAddress.Parse(MulticastAddressV4);
        var multicastIpV6 = IPAddress.Parse(MulticastAddressV6);
        var joinedIpv4Interfaces = new HashSet<string>(StringComparer.Ordinal);
        var joinedIpv6Interfaces = new HashSet<int>();

        try
        {
            _udpClientV4.MulticastLoopback = true;
            _udpClientV6?.MulticastLoopback = true;
            RefreshDiscoveryMulticastMembership(
                _udpClientV4,
                _udpClientV6,
                multicastIpV4,
                multicastIpV6,
                joinedIpv4Interfaces,
                joinedIpv6Interfaces,
                true);
            DeviceCommunicationDiagnostics.Info(
                LogCategory,
                $"Discovery multicast membership: {MulticastAddressV4} on {joinedIpv4Interfaces.Count} IPv4 interfaces, {MulticastAddressV6} on {joinedIpv6Interfaces.Count} IPv6 interfaces.");
        }
        catch (Exception exception)
        {
            DeviceCommunicationDiagnostics.Error(LogCategory, "Failed to join discovery multicast groups.", exception);
        }

        var receiveTasks = new List<Task> { ReceiveLoopAsync(_udpClientV4, token) };
        if (_udpClientV6 is not null)
        {
            receiveTasks.Add(ReceiveLoopAsync(_udpClientV6, token));
        }

        receiveTasks.Add(RefreshDiscoveryMulticastMembershipLoopAsync(
            _udpClientV4,
            _udpClientV6,
            multicastIpV4,
            multicastIpV6,
            joinedIpv4Interfaces,
            joinedIpv6Interfaces,
            token));

        await Task.WhenAll(receiveTasks);
    }

    private async Task RefreshDiscoveryMulticastMembershipLoopAsync(
        UdpClient udpClientV4,
        UdpClient? udpClientV6,
        IPAddress multicastIpV4,
        IPAddress multicastIpV6,
        HashSet<string> joinedIpv4Interfaces,
        HashSet<int> joinedIpv6Interfaces,
        CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Scaled(_discoveryListenerRefreshInterval), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            RefreshDiscoveryMulticastMembership(
                udpClientV4,
                udpClientV6,
                multicastIpV4,
                multicastIpV6,
                joinedIpv4Interfaces,
                joinedIpv6Interfaces,
                false);
        }
    }

    private void RefreshDiscoveryMulticastMembership(
        UdpClient udpClientV4,
        UdpClient? udpClientV6,
        IPAddress multicastIpV4,
        IPAddress multicastIpV6,
        HashSet<string> joinedIpv4Interfaces,
        HashSet<int> joinedIpv6Interfaces,
        bool logInterfaces)
    {
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                !networkInterface.SupportsMulticast ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            var properties = networkInterface.GetIPProperties();
            if (logInterfaces)
            {
                DeviceCommunicationDiagnostics.Info(
                    LogCategory,
                    $"Interface {networkInterface.Name} ({networkInterface.NetworkInterfaceType}) Addresses={string.Join(", ", properties.UnicastAddresses.Select(address => address.Address.ToString()))}.");
            }

            var ipv4Address = properties.UnicastAddresses
                .Where(IsUsableDiscoveryAddress)
                .Select(unicast => unicast.Address)
                .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);
            if (ipv4Address is not null && joinedIpv4Interfaces.Add(networkInterface.Id))
            {
                try
                {
                    udpClientV4.JoinMulticastGroup(multicastIpV4, ipv4Address);
                    DeviceCommunicationDiagnostics.Info(
                        LogCategory,
                        $"Joined IPv4 multicast {multicastIpV4} on {networkInterface.Name} {ipv4Address}.");
                }
                catch (Exception exception)
                {
                    if (logInterfaces)
                    {
                        DeviceCommunicationDiagnostics.Warning(
                            LogCategory,
                            $"Failed to join IPv4 multicast on {networkInterface.Name} {ipv4Address}: {exception.Message}");
                    }

                    joinedIpv4Interfaces.Remove(networkInterface.Id);
                }
            }

            if (udpClientV6 is null)
            {
                continue;
            }

            var ipv6InterfaceIndex = 0;
            try
            {
                if (!properties.UnicastAddresses.Any(address =>
                        address.Address.AddressFamily == AddressFamily.InterNetworkV6 && IsUsableDiscoveryAddress(address)))
                {
                    continue;
                }

                var ipv6Properties = properties.GetIPv6Properties();
                if (ipv6Properties is null || ipv6Properties.Index <= 0)
                {
                    continue;
                }

                ipv6InterfaceIndex = ipv6Properties.Index;
                if (!joinedIpv6Interfaces.Add(ipv6InterfaceIndex))
                {
                    continue;
                }

                udpClientV6.JoinMulticastGroup(ipv6InterfaceIndex, multicastIpV6);
                DeviceCommunicationDiagnostics.Info(
                    LogCategory,
                    $"Joined IPv6 multicast {multicastIpV6} on {networkInterface.Name} interfaceIndex={ipv6InterfaceIndex}.");
            }
            catch (Exception exception)
            {
                if (logInterfaces)
                {
                    DeviceCommunicationDiagnostics.Warning(
                        LogCategory,
                        $"Failed to join IPv6 multicast on {networkInterface.Name}: {exception.Message}");
                }

                if (ipv6InterfaceIndex > 0)
                {
                    joinedIpv6Interfaces.Remove(ipv6InterfaceIndex);
                }
            }
        }
    }

    private static bool IsUsableDiscoveryAddress(UnicastIPAddressInformation unicast)
    {
        var address = unicast.Address;
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.None) ||
            address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None)) return false;

        // Address readiness is exposed only on Windows; other platforms select the source at send time.
        return !OperatingSystem.IsWindows() ||
               unicast.DuplicateAddressDetectionState == DuplicateAddressDetectionState.Preferred;
    }

    private async Task ReceiveLoopAsync(UdpClient client, CancellationToken token)
    {
        var localEndPoint = client.Client.LocalEndPoint;
        DeviceCommunicationDiagnostics.Info(
            LogCategory,
            $"Discovery receive loop started on {localEndPoint} ({client.Client.AddressFamily}) pid={Environment.ProcessId}.");
        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await client.ReceiveAsync(token);
                var info = JsonSerializer.Deserialize(
                    result.Buffer.AsSpan(),
                    DeviceCommunicationJsonSerializerContext.Default.DiscoveryInfo);
                if (info is null)
                {
                    DeviceCommunicationDiagnostics.Warning(
                        LogCategory,
                        $"Ignored discovery datagram from {result.RemoteEndPoint}: JSON payload is null.");
                    continue;
                }

                if (!IsSupportedDiscoveryVersion(info.Version))
                {
                    DeviceCommunicationDiagnostics.Warning(
                        LogCategory,
                        $"Ignored discovery datagram from {result.RemoteEndPoint}: unsupported protocol version '{info.Version}', local='{DiscoveryProtocolVersion}'.");
                    continue;
                }

                if (!TryGetLocalIdentity(out var localIdentity))
                {
                    DeviceCommunicationDiagnostics.Warning(
                        LogCategory,
                        $"Ignored discovery datagram from {result.RemoteEndPoint}: local device identity is unavailable.");
                    continue;
                }

                var endpointAddress = NormalizeAddress(result.RemoteEndPoint.Address);
                var messageType = NormalizeMessageType(info.MessageType);
                if (messageType != DiscoveryMessageTypeAnnounce)
                {
                    DeviceCommunicationDiagnostics.Debug(
                        LogCategory,
                        $"Received {messageType} from {endpointAddress} id={ShortId(info.Id)} tcp={info.TcpPort}.");
                }

                switch (messageType)
                {
                    case DiscoveryMessageTypeAnnounce:
                        await HandleAnnouncementAsync(info, endpointAddress, localIdentity.IdHash, token);
                        break;
                    case DiscoveryMessageTypeAuthRequest:
                        await HandleAuthRequestAsync(info, endpointAddress, localIdentity.IdHash, localIdentity.PublicKey, token);
                        break;
                    case DiscoveryMessageTypeAuthResponse:
                        await HandleAuthResponseAsync(
                            info,
                            endpointAddress,
                            localIdentity.PublicKey,
                            localIdentity.IdHash,
                            token);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                DeviceCommunicationDiagnostics.Error(LogCategory, $"Receive loop error on {localEndPoint}.", exception);
            }
        }

        DeviceCommunicationDiagnostics.Info(
            LogCategory,
            $"Discovery receive loop stopped on {localEndPoint}. CancellationRequested={token.IsCancellationRequested}.");
    }

    private async Task BroadcastLoopAsync(CancellationToken token)
    {
        var multicastIpV4 = IPAddress.Parse(MulticastAddressV4);
        var multicastIpV6 = IPAddress.Parse(MulticastAddressV6);
        var multicastEndpointV4 = new IPEndPoint(multicastIpV4, DiscoveryPort);

        while (!token.IsCancellationRequested)
        {
            try
            {
                var tcpPort = _localDataEndpointProvider.TcpPort;
                var hasIdentity = TryGetLocalIdentity(out var identity);
                if (tcpPort <= 0 || !hasIdentity)
                {
                    DeviceCommunicationDiagnostics.Debug(
                        LogCategory,
                        $"Broadcast skipped. TcpPort={tcpPort}, HasIdentity={hasIdentity}.");
                }
                else
                {
                    var info = new DiscoveryInfo
                    {
                        MessageType = DiscoveryMessageTypeAnnounce,
                        Version = DiscoveryProtocolVersion,
                        Id = identity.IdHash,
                        Name = ResolveDisplayName(),
                        OperatingSystem = ResolveOperatingSystemName(),
                        TcpPort = tcpPort,
                        TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        PublicKey = identity.PublicKey
                    };
                    if (!DeviceDiscoverySignature.TrySign(info, identity.PrivateKey, out var signature))
                    {
                        DeviceCommunicationDiagnostics.Warning(LogCategory, "Failed to sign discovery announcement.");
                        await Task.Delay(Scaled(_discoveryBroadcastInterval), token);
                        continue;
                    }

                    info.Signature = signature;
                    var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                        info,
                        DeviceCommunicationJsonSerializerContext.Default.DiscoveryInfo));

                    foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                            !networkInterface.SupportsMulticast ||
                            networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        {
                            continue;
                        }

                        IPInterfaceProperties properties;
                        try
                        {
                            properties = networkInterface.GetIPProperties();
                        }
                        catch (NetworkInformationException exception)
                        {
                            DeviceCommunicationDiagnostics.Debug(
                                LogCategory,
                                $"Interface {networkInterface.Name} unavailable during announce: {exception.Message}");
                            continue;
                        }

                        var ipv4Address = properties.UnicastAddresses
                            .Where(IsUsableDiscoveryAddress)
                            .Select(unicast => unicast.Address)
                            .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);
                        if (ipv4Address is not null)
                        {
                            try
                            {
                                using var client = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
                                client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface,
                                    ipv4Address.GetAddressBytes());
                                client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive,
                                    DiscoveryMulticastHopLimit);
                                await client.SendAsync(bytes, multicastEndpointV4, token);
                            }
                            catch (SocketException exception) when (exception.SocketErrorCode is
                                       SocketError.AddressNotAvailable or SocketError.NetworkDown or SocketError.NetworkUnreachable)
                            {
                                DeviceCommunicationDiagnostics.Debug(
                                    LogCategory,
                                    $"IPv4 announce skipped on unavailable interface {networkInterface.Name} {ipv4Address}: {exception.SocketErrorCode}.");
                            }
                            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                            catch (Exception exception)
                            {
                                DeviceCommunicationDiagnostics.Warning(
                                    LogCategory,
                                    $"IPv4 announce send failed on {networkInterface.Name} {ipv4Address}: {exception.Message}");
                            }
                        }

                        if (!properties.UnicastAddresses.Any(unicast =>
                                unicast.Address.AddressFamily == AddressFamily.InterNetworkV6 && IsUsableDiscoveryAddress(unicast)))
                            continue;

                        try
                        {
                            var interfaceIndex = properties.GetIPv6Properties()?.Index ?? 0;
                            if (interfaceIndex <= 0) continue;
                            using var client = new UdpClient(AddressFamily.InterNetworkV6);
                            client.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
                            client.Client.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
                            client.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface, interfaceIndex);
                            client.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastTimeToLive,
                                DiscoveryMulticastHopLimit);
                            var multicastAddressV6WithScope = new IPAddress(multicastIpV6.GetAddressBytes(), interfaceIndex);
                            await client.SendAsync(bytes, new IPEndPoint(multicastAddressV6WithScope, DiscoveryPort), token);
                        }
                        catch (NetworkInformationException exception)
                        {
                            DeviceCommunicationDiagnostics.Debug(
                                LogCategory,
                                $"IPv6 announce skipped on unavailable interface {networkInterface.Name}: {exception.Message}");
                        }
                        catch (SocketException exception) when (exception.SocketErrorCode is
                                   SocketError.AddressNotAvailable or SocketError.NetworkDown or SocketError.NetworkUnreachable)
                        {
                            DeviceCommunicationDiagnostics.Debug(
                                LogCategory,
                                $"IPv6 announce skipped on unavailable interface {networkInterface.Name}: {exception.SocketErrorCode}.");
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                        catch (Exception exception)
                        {
                            DeviceCommunicationDiagnostics.Warning(
                                LogCategory,
                                $"IPv6 announce send failed on {networkInterface.Name}: {exception.Message}");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                DeviceCommunicationDiagnostics.Warning(LogCategory, $"Broadcast loop error: {exception.Message}");
            }

            try
            {
                await Task.Delay(Scaled(_discoveryBroadcastInterval), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task HandleAnnouncementAsync(
        DiscoveryInfo info,
        IPAddress endpointAddress,
        string localIdHash,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(info.Id) ||
            info.TcpPort <= 0 ||
            string.Equals(info.Id, localIdHash, StringComparison.Ordinal))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(info.Signature))
        {
            if (!DeviceDiscoverySignature.VerifyAnnouncement(
                    info,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    DiscoverySignatureToleranceSeconds))
            {
                DeviceCommunicationDiagnostics.Warning(
                    LogCategory,
                    $"Rejected discovery announcement from {endpointAddress}: invalid signature.");
                return;
            }

            UpsertAuthenticatedDevice(info, endpointAddress);
            return;
        }

        if (TryRefreshKnownDeviceFromAnnouncement(info, endpointAddress))
        {
            return;
        }

        if (HasKnownIdentity(info.Id, info.TcpPort))
        {
            return;
        }

        if (HasPendingAuthRequest(info.Id, endpointAddress))
        {
            return;
        }

        var nonce = CreateNonce();
        RegisterPendingAuthRequest(info.Id, nonce, endpointAddress);
        DeviceCommunicationDiagnostics.Info(
            LogCategory,
            $"Announcement accepted from {endpointAddress}. Requesting auth for {ShortId(info.Id)} nonce={ShortId(nonce)}.");

        var request = new DiscoveryInfo
        {
            MessageType = DiscoveryMessageTypeAuthRequest,
            Version = string.Equals(info.Version, "0.1", StringComparison.Ordinal)
                ? "0.1"
                : DiscoveryProtocolVersion,
            Id = info.Id,
            TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Nonce = nonce
        };

        await SendUnicastAsync(request, new IPEndPoint(endpointAddress, DiscoveryPort), token);
    }

    private bool TryRefreshKnownDeviceFromAnnouncement(DiscoveryInfo info, IPAddress endpointAddress)
    {
        lock (_sync)
        {
            CleanupPendingAuthRequests(DateTime.UtcNow);

            var existing = _devicesSource.FirstOrDefault(device =>
                IsSameIdentityHash(device, info.Id));
            if (existing is null || existing.TcpPort != info.TcpPort ||
                !TouchVerifiedEndpoint(existing.Id, endpointAddress))
            {
                return false;
            }

            existing.LastSeen = DateTime.UtcNow;
            existing.Name = string.IsNullOrWhiteSpace(info.Name) ? "Unknown device" : info.Name.Trim();
            existing.OperatingSystem = NormalizeOperatingSystemName(info.OperatingSystem);
            existing.TcpPort = info.TcpPort;
            AssignDiscoveredAddress(existing, endpointAddress);
            return true;
        }
    }

    private async Task HandleAuthRequestAsync(
        DiscoveryInfo info,
        IPAddress endpointAddress,
        string localIdHash,
        string localPublicKey,
        CancellationToken token)
    {
        if (!string.Equals(info.Id, localIdHash, StringComparison.Ordinal))
        {
            DeviceCommunicationDiagnostics.Debug(
                LogCategory,
                $"Ignored auth request from {endpointAddress}: target id={ShortId(info.Id)} does not match local id={ShortId(localIdHash)}.");
            return;
        }

        if (string.IsNullOrWhiteSpace(info.Nonce))
        {
            DeviceCommunicationDiagnostics.Warning(LogCategory, $"Ignored auth request from {endpointAddress}: nonce is missing.");
            return;
        }

        var tcpPort = _localDataEndpointProvider.TcpPort;
        if (tcpPort <= 0)
        {
            DeviceCommunicationDiagnostics.Warning(LogCategory, $"Ignored auth request from {endpointAddress}: local TCP listener is unavailable.");
            return;
        }

        if (!_identityStore.TryGetIdentity(out var identity))
        {
            DeviceCommunicationDiagnostics.Warning(LogCategory, $"Ignored auth request from {endpointAddress}: local device identity is unavailable.");
            return;
        }

        var response = new DiscoveryInfo
        {
            MessageType = DiscoveryMessageTypeAuthResponse,
            Version = string.Equals(info.Version, "0.1", StringComparison.Ordinal)
                ? "0.1"
                : DiscoveryProtocolVersion,
            Id = localIdHash,
            Name = ResolveDisplayName(),
            OperatingSystem = ResolveOperatingSystemName(),
            TcpPort = tcpPort,
            TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            PublicKey = localPublicKey,
            Nonce = info.Nonce
        };

        if (!DeviceDiscoverySignature.TrySign(response, identity.PrivateKey, out var signature))
        {
            DeviceCommunicationDiagnostics.Warning(LogCategory, "Failed to sign auth response.");
            return;
        }

        response.Signature = signature;
        DeviceCommunicationDiagnostics.Info(
            LogCategory,
            $"Auth request matched for {endpointAddress}. Responding with TcpPort={tcpPort}.");
        await SendUnicastAsync(response, new IPEndPoint(endpointAddress, DiscoveryPort), token);
    }

    private Task HandleAuthResponseAsync(
        DiscoveryInfo info,
        IPAddress endpointAddress,
        string localPublicKey,
        string localIdHash,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(info.Id) ||
            string.IsNullOrWhiteSpace(info.PublicKey) ||
            string.IsNullOrWhiteSpace(info.Signature) ||
            info.TcpPort <= 0 ||
            string.Equals(info.PublicKey, localPublicKey, StringComparison.Ordinal) ||
            string.Equals(info.Id, localIdHash, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        if (!TryGetPendingAuthRequest(info.Id, info.Nonce, out var pending) ||
            !IsAuthenticated(info, pending.Nonce))
        {
            DeviceCommunicationDiagnostics.Warning(
                LogCategory,
                $"Rejected auth response from {endpointAddress} id={ShortId(info.Id)} pending=" +
                $"{HasPendingAuthRequest(info.Id, endpointAddress)}.");
            return Task.CompletedTask;
        }

        RemovePendingAuthRequest(info.Id, pending.Nonce);
        DeviceCommunicationDiagnostics.Info(
            LogCategory,
            $"Authenticated device {ShortId(info.PublicKey)} from {endpointAddress}:{info.TcpPort} " +
            $"using nonce={ShortId(pending.Nonce)}.");
        UpsertAuthenticatedDevice(info, endpointAddress);
        return Task.CompletedTask;
    }

    private void UpsertAuthenticatedDevice(DiscoveryInfo info, IPAddress endpointAddress)
    {
        lock (_sync)
        {
            CleanupPendingAuthRequests(DateTime.UtcNow);

            var existing = _devicesSource.FirstOrDefault(device =>
                string.Equals(device.Id, info.PublicKey, StringComparison.Ordinal));
            if (existing is null)
            {
                var duplicateEndpoint = _devicesSource.FirstOrDefault(device =>
                    IsSameEndpoint(device, endpointAddress) &&
                    device.TcpPort == info.TcpPort);
                if (duplicateEndpoint is not null)
                {
                    _devicesSource.Remove(duplicateEndpoint);
                    _verifiedEndpoints.Remove(duplicateEndpoint.Id);
                }

                existing = new DiscoveredDevice
                {
                    Id = info.PublicKey,
                    Name = string.IsNullOrWhiteSpace(info.Name) ? "Unknown device" : info.Name.Trim(),
                    OperatingSystem = NormalizeOperatingSystemName(info.OperatingSystem),
                    CustomName = _settings.GetCustomName(info.PublicKey) ?? string.Empty,
                    TcpPort = info.TcpPort,
                    LastSeen = DateTime.UtcNow
                };
                AssignDiscoveredAddress(existing, endpointAddress);
                AddVerifiedEndpoint(existing.Id, endpointAddress, existing.LastSeen);
                _devicesSource.Add(existing);
                DeviceCommunicationDiagnostics.Info(
                    LogCategory,
                    $"Added device {ShortId(existing.Id)} name={existing.Name} endpoint={FormatEndpoint(existing)} tcp={existing.TcpPort}.");
            }
            else
            {
                existing.LastSeen = DateTime.UtcNow;
                existing.Name = string.IsNullOrWhiteSpace(info.Name) ? "Unknown device" : info.Name.Trim();
                existing.OperatingSystem = NormalizeOperatingSystemName(info.OperatingSystem);
                existing.TcpPort = info.TcpPort;
                AssignDiscoveredAddress(existing, endpointAddress);
                AddVerifiedEndpoint(existing.Id, endpointAddress, existing.LastSeen);
                DeviceCommunicationDiagnostics.Debug(
                    LogCategory,
                    $"Updated device {ShortId(existing.Id)} endpoint={FormatEndpoint(existing)} tcp={existing.TcpPort}.");
            }
        }
    }

    private bool TouchVerifiedEndpoint(string deviceId, IPAddress endpointAddress)
    {
        if (!_verifiedEndpoints.TryGetValue(deviceId, out var endpoints))
        {
            return false;
        }

        var endpoint = endpoints.FirstOrDefault(item => item.Address.Equals(endpointAddress));
        if (endpoint is null)
        {
            return false;
        }

        endpoint.LastSeenUtc = DateTime.UtcNow;
        return true;
    }

    private void AddVerifiedEndpoint(string deviceId, IPAddress endpointAddress, DateTime lastSeenUtc)
    {
        if (!_verifiedEndpoints.TryGetValue(deviceId, out var endpoints))
        {
            endpoints = [];
            _verifiedEndpoints[deviceId] = endpoints;
        }

        var existing = endpoints.FirstOrDefault(item => item.Address.Equals(endpointAddress));
        if (existing is null)
        {
            endpoints.Add(new VerifiedEndpoint(endpointAddress, lastSeenUtc));
        }
        else
        {
            existing.LastSeenUtc = lastSeenUtc;
        }
    }

    private void CleanupVerifiedEndpoints(DateTime nowUtc)
    {
        var staleTimeout = Scaled(_discoveryStaleTimeoutBase);
        foreach (var pair in _verifiedEndpoints.ToArray())
        {
            pair.Value.RemoveAll(endpoint => nowUtc - endpoint.LastSeenUtc > staleTimeout);
            if (pair.Value.Count == 0)
            {
                _verifiedEndpoints.Remove(pair.Key);
                if (_devicesSource.FirstOrDefault(device => string.Equals(device.Id, pair.Key, StringComparison.Ordinal)) is { } staleDevice)
                {
                    staleDevice.Ipv4Address = IPAddress.None;
                    staleDevice.Ipv6Address = IPAddress.None;
                }
                continue;
            }

            if (_devicesSource.FirstOrDefault(device => string.Equals(device.Id, pair.Key, StringComparison.Ordinal)) is { } device)
            {
                RefreshDeviceAddresses(device, pair.Value);
            }
        }
    }

    private static void RefreshDeviceAddresses(DiscoveredDevice device, IEnumerable<VerifiedEndpoint> endpoints)
    {
        var addresses = endpoints
            .OrderByDescending(endpoint => endpoint.LastSeenUtc)
            .Select(endpoint => endpoint.Address)
            .ToArray();
        device.Ipv4Address = addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork)
                             ?? IPAddress.None;
        device.Ipv6Address = addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetworkV6)
                             ?? IPAddress.None;
    }

    private bool TryGetPendingAuthRequest(string id, string nonce, out PendingAuthRequest pending)
    {
        lock (_sync)
        {
            CleanupPendingAuthRequests(DateTime.UtcNow);
            if (_pendingAuthRequests.TryGetValue(id, out var requests))
            {
                var request = requests.FirstOrDefault(item =>
                    string.Equals(item.Nonce, nonce, StringComparison.Ordinal));
                if (!string.IsNullOrWhiteSpace(request.Nonce))
                {
                    pending = request;
                    return true;
                }
            }

            pending = default;
            return false;
        }
    }

    private void RemovePendingAuthRequest(string id, string nonce)
    {
        lock (_sync)
        {
            if (!_pendingAuthRequests.TryGetValue(id, out var requests))
            {
                return;
            }

            requests.RemoveAll(item => string.Equals(item.Nonce, nonce, StringComparison.Ordinal));
            if (requests.Count == 0)
            {
                _pendingAuthRequests.Remove(id);
            }
        }
    }

    private bool HasPendingAuthRequest(string id, IPAddress endpointAddress)
    {
        lock (_sync)
        {
            CleanupPendingAuthRequests(DateTime.UtcNow);
            return _pendingAuthRequests.TryGetValue(id, out var requests) &&
                   requests.Any(request => request.RequestedAddress.Equals(endpointAddress));
        }
    }

    private bool HasKnownIdentity(string idHash, int tcpPort)
    {
        lock (_sync)
        {
            var device = _devicesSource.FirstOrDefault(candidate =>
                candidate.TcpPort == tcpPort && IsSameIdentityHash(candidate, idHash));
            if (device is null ||
                !_verifiedEndpoints.TryGetValue(device.Id, out var endpoints) ||
                endpoints.Count == 0)
            {
                return false;
            }

            var now = DateTime.UtcNow;
            return endpoints.Any(endpoint => now - endpoint.LastSeenUtc <= Scaled(_discoveryStaleTimeoutBase));
        }
    }

    private void RegisterPendingAuthRequest(string id, string nonce, IPAddress endpointAddress)
    {
        lock (_sync)
        {
            CleanupPendingAuthRequests(DateTime.UtcNow);
            if (!_pendingAuthRequests.TryGetValue(id, out var requests))
            {
                requests = [];
                _pendingAuthRequests[id] = requests;
            }

            requests.Add(new PendingAuthRequest(
                nonce,
                DateTime.UtcNow + Scaled(_discoveryAuthRequestTimeoutBase),
                endpointAddress));
        }
    }

    private void CleanupPendingAuthRequests(DateTime nowUtc)
    {
        if (_pendingAuthRequests.Count == 0)
        {
            return;
        }

        var expiredKeys = _pendingAuthRequests
            .Where(pair =>
            {
                pair.Value.RemoveAll(request => request.ExpiresAtUtc <= nowUtc);
                return pair.Value.Count == 0;
            })
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var key in expiredKeys)
        {
            _pendingAuthRequests.Remove(key);
        }
    }

    private static string NormalizeMessageType(string? messageType)
    {
        return string.IsNullOrWhiteSpace(messageType)
            ? DiscoveryMessageTypeAnnounce
            : messageType.Trim().ToLowerInvariant();
    }

    private static bool IsSupportedDiscoveryVersion(string? version)
    {
        return Version.TryParse(version?.Trim(), out var remoteVersion) &&
               DiscoveryProtocolVersionValue >= remoteVersion;
    }

    private static string CreateNonce()
    {
        return Guid.NewGuid().ToString("N");
    }

    private static bool IsAuthenticated(DiscoveryInfo info, string expectedNonce)
    {
        return DeviceDiscoverySignature.VerifyAuthResponse(
            info,
            expectedNonce,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            DiscoverySignatureToleranceSeconds);
    }

    private bool TryGetLocalIdentity(out DeviceIdentity identity)
    {
        return _identityStore.TryGetIdentity(out identity);
    }

    private string ResolveDisplayName()
    {
        return string.IsNullOrWhiteSpace(_settings.BroadcastName)
            ? Environment.MachineName
            : _settings.BroadcastName.Trim();
    }

    private string ResolveOperatingSystemName()
    {
        return NormalizeOperatingSystemName(_settings.OperatingSystemName);
    }

    private static string NormalizeOperatingSystemName(string? operatingSystem)
    {
        return string.IsNullOrWhiteSpace(operatingSystem)
            ? string.Empty
            : operatingSystem.Trim();
    }

    private static async Task SendUnicastAsync(
        DiscoveryInfo info,
        IPEndPoint remoteEndPoint,
        CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            info,
            DeviceCommunicationJsonSerializerContext.Default.DiscoveryInfo));
        using var client = remoteEndPoint.AddressFamily == AddressFamily.InterNetworkV6
            ? new UdpClient(AddressFamily.InterNetworkV6)
            : new UdpClient(AddressFamily.InterNetwork);
        await client.SendAsync(bytes, bytes.Length, remoteEndPoint).WaitAsync(token);
        DeviceCommunicationDiagnostics.Debug(
            LogCategory,
            $"Sent {NormalizeMessageType(info.MessageType)} to {remoteEndPoint} from socket {client.Client.LocalEndPoint} bytes={bytes.Length}.");
    }

    private static bool IsSameEndpoint(DiscoveredDevice device, IPAddress endpointAddress)
    {
        return device.Ipv4Address.Equals(endpointAddress) ||
               device.Ipv6Address.Equals(endpointAddress);
    }

    private static bool IsSameIdentityHash(DiscoveredDevice device, string idHash)
    {
        return !string.IsNullOrWhiteSpace(device.Id) &&
               string.Equals(
                   DeviceDiscoverySignature.ComputePublicKeyHash(device.Id),
                   idHash,
                   StringComparison.Ordinal);
    }

    private static void AssignDiscoveredAddress(DiscoveredDevice device, IPAddress endpointAddress)
    {
        if (endpointAddress.AddressFamily == AddressFamily.InterNetwork)
        {
            device.Ipv4Address = endpointAddress;
        }
        else if (endpointAddress.AddressFamily == AddressFamily.InterNetworkV6)
        {
            device.Ipv6Address = endpointAddress;
        }
    }

    private static IPAddress NormalizeAddress(IPAddress address)
    {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    private static string ShortId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "n/a";
        }

        const int visibleLength = 10;
        return value.Length <= visibleLength ? value : value[..visibleLength];
    }

    private static string FormatEndpoint(DiscoveredDevice device)
    {
        if (device.Ipv4Address != IPAddress.None)
        {
            return device.Ipv4Address.ToString();
        }

        if (device.Ipv6Address != IPAddress.None)
        {
            return device.Ipv6Address.ToString();
        }

        return "unknown";
    }
}
