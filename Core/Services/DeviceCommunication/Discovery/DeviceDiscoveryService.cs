using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Services.Config;
using Core.Services.DeviceCommunication;
using DynamicData;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Serilog;
using Serilog.Core;

namespace Core.Services.DeviceCommunication.Discovery;

public sealed class DeviceDiscoveryService : IDeviceDiscoveryService {
    private static readonly ILogger Logger = LogManager.Logger.ForContext<IDeviceDiscoveryService>();
    private const int DiscoveryPort = 53535;
    private const string MulticastAddressV4 = "239.255.255.250";
    private const string MulticastAddressV6 = "ff02::1";
    private const int DiscoveryIpv4Ttl = 1;
    private const long DiscoverySignatureToleranceSeconds = 60;
    private static readonly TimeSpan DiscoveryBroadcastInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DiscoveryCleanupInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DiscoveryListenerRefreshInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DiscoveryStaleTimeout = TimeSpan.FromSeconds(20);

    private readonly object _sync = new();
    private readonly ObservableCollection<DeviceModel> _devices = [];

    private CancellationTokenSource? _cts;
    private UdpClient? _udpClientV4;
    private UdpClient? _udpClientV6;

    public ObservableCollection<DeviceModel> Devices => _devices;
    

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

    public void Dispose() {
        StopAsync().GetAwaiter().GetResult();
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
        _devices.Clear();
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
                var staleDevices = _devices
                    .Where(device => now - device.LastSeen > DiscoveryStaleTimeout);
                foreach (var staleDevice in staleDevices) {
                    _devices.Remove(staleDevice);
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
                if (info is null || string.IsNullOrWhiteSpace(info.Id) || info.TcpPort <= 0 ||
                    info is { SupportsQuic: true, QuicPort: <= 0 }) {
                    continue;
                }

                if (string.Equals(info.Id, ConfigManger.Config.devicePersistentId, StringComparison.Ordinal)) {
                    continue;
                }

                if (!IsAuthenticated(info)) {
                    continue;
                }
                var endpointAddress = NormalizeAddress(result.RemoteEndPoint.Address);
                
                lock (_sync) {
                    var existing = _devices.FirstOrDefault(device =>
                        string.Equals(device.Id, info.Id, StringComparison.Ordinal));
                    if (existing is null) {
                        var duplicateEndpoint = _devices.FirstOrDefault(device =>
                            device.Address.Equals(endpointAddress) && device.TcpPort == info.TcpPort&& device.SupportQuic == info.SupportsQuic);
                        if (duplicateEndpoint is not null) {
                            _devices.Remove(duplicateEndpoint);
                        }

                        existing = new DeviceModel {
                            Id = info.Id,
                            Name = string.IsNullOrWhiteSpace(info.Name) ? "未知设备" : info.Name.Trim(),
                            CustomName = ConfigManger.Config.deviceCustomNames.TryGetValue(info.Id, out var customName) ? customName : string.Empty,
                            Address = endpointAddress,
                            TcpPort = info.TcpPort,
                            QuicPort = info.QuicPort,
                            SupportQuic = info.SupportsQuic,
                            LastSeen = DateTime.UtcNow
                        };
                        _devices.Add(existing);
                    }
                    else {
                        existing.LastSeen = DateTime.UtcNow;
                        existing.Name = string.IsNullOrWhiteSpace(info.Name) ? "未知设备" : info.Name.Trim();
                        if (ShouldReplaceDiscoveredAddress(existing.Address, endpointAddress)) {
                            existing.Address = endpointAddress;
                        }

                        existing.TcpPort = info.TcpPort;
                        existing.QuicPort = info.QuicPort;
                        existing.SupportQuic = info.SupportsQuic;
                    }
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

                var info = new DiscoveryInfo {
                    Id = ConfigManger.Config.devicePersistentId,
                    Name = string.IsNullOrWhiteSpace(ConfigManger.Config.deviceBroadcastName)? Environment.MachineName : ConfigManger.Config.deviceBroadcastName.Trim(),
                    TcpPort = tcpPort,
                    QuicPort = localDataListener.QuicPort,
                    SupportsQuic = localDataListener.SupportsQuic,
                    TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };

                if (string.IsNullOrWhiteSpace(info.Id) ||
                    !DeviceDiscoverySignature.TrySign(info, ConfigManger.Config.devicePrivateKey, out var signature)) {
                    continue;
                }

                info.Signature = signature;
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

    private static bool IsAuthenticated(DiscoveryInfo info) {
        if (string.IsNullOrWhiteSpace(info.Signature)) {
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
    
    private static IPAddress NormalizeAddress(IPAddress address) {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    private static bool ShouldReplaceDiscoveredAddress(IPAddress currentAddress, IPAddress candidateAddress) {
        if (currentAddress.Equals(candidateAddress)) {
            return false;
        }

        var currentFamily = currentAddress.AddressFamily;
        var candidateFamily = candidateAddress.AddressFamily;

        if (currentFamily == AddressFamily.InterNetwork && candidateFamily == AddressFamily.InterNetworkV6) {
            return false;
        }

        if (currentFamily == AddressFamily.InterNetworkV6 && candidateFamily == AddressFamily.InterNetwork) {
            return true;
        }

        return true;
    }
}


