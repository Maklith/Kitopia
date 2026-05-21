using System.Net;
using System.Net.Sockets;
using Core.Services.DeviceCommunication.Discovery;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Routing;
using PluginCore;

namespace Core.Services.DeviceCommunication;

public sealed class DeviceTransportService
{
    private readonly ILocalDataListener _localDataListener;
    private readonly ProtocolSender _protocolSender;
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;

    public DeviceTransportService(
        ILocalDataListener localDataListener,
        ProtocolSender protocolSender,
        IDeviceDiscoveryService deviceDiscoveryService)
    {
        _localDataListener = localDataListener;
        _protocolSender = protocolSender;
        _deviceDiscoveryService = deviceDiscoveryService;
    }

    public Task SendAsync(
        string deviceId,
        DataEnvelope envelope,
        Stream? payloadStream = null,
        Func<long, long, ValueTask>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var device = ResolveDevice(deviceId);
        return SendAsync(device, envelope, payloadStream, progressCallback, cancellationToken);
    }

    private async Task SendAsync(
        DeviceModel device,
        DataEnvelope envelope,
        Stream? payloadStream,
        Func<long, long, ValueTask>? progressCallback,
        CancellationToken cancellationToken)
    {
        var address = SelectTransportAddress(device);
        var primaryProtocol = SelectTransportProtocol(device);
        var primaryPort = ResolvePort(device, primaryProtocol);
        if (primaryPort <= 0 || address == IPAddress.None)
        {
            throw new InvalidOperationException("Invalid target address or port.");
        }

        try
        {
            await SendCoreAsync(device.Id, primaryProtocol, address, primaryPort, envelope, payloadStream, progressCallback,
                cancellationToken);
        }
        catch (Exception) when (primaryProtocol == LocalDataTransportProtocol.Quic && device.TcpPort > 0)
        {
            await SendCoreAsync(device.Id, LocalDataTransportProtocol.Tcp, address, device.TcpPort, envelope, payloadStream,
                progressCallback, cancellationToken);
        }
    }

    private Task SendCoreAsync(
        string deviceId,
        LocalDataTransportProtocol protocol,
        IPAddress address,
        int port,
        DataEnvelope envelope,
        Stream? payloadStream,
        Func<long, long, ValueTask>? progressCallback,
        CancellationToken cancellationToken)
    {
        var context = new MessageContext(protocol, new IPEndPoint(address, port), deviceId);
        return _protocolSender.SendAsync(context, envelope, payloadStream, progressCallback, cancellationToken);
    }

    private DeviceModel ResolveDevice(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new InvalidOperationException("Invalid target device identity.");
        }

        var device = _deviceDiscoveryService.Devices.FirstOrDefault(item =>
            string.Equals(item.Id, deviceId, StringComparison.Ordinal));
        if (device is null)
        {
            throw new InvalidOperationException("Target device is not available.");
        }

        return device;
    }

    private LocalDataTransportProtocol SelectTransportProtocol(DeviceModel device)
    {
        return _localDataListener.SupportsQuic && device.SupportQuic && device.QuicPort > 0
            ? LocalDataTransportProtocol.Quic
            : LocalDataTransportProtocol.Tcp;
    }

    private static int ResolvePort(DeviceModel device, LocalDataTransportProtocol protocol)
    {
        return protocol == LocalDataTransportProtocol.Quic ? device.QuicPort : device.TcpPort;
    }

    private static IPAddress SelectTransportAddress(DeviceModel device)
    {
        if (Socket.OSSupportsIPv6 && device.Ipv6Address != IPAddress.None)
        {
            return device.Ipv6Address;
        }

        return device.Ipv4Address;
    }
}
