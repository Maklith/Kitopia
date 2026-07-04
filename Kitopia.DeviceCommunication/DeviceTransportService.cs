using System.Net;
using System.Net.Sockets;
using Kitopia.DeviceCommunication.Discovery;
using Kitopia.DeviceCommunication.Protocol;
using Kitopia.DeviceCommunication.Transport;

namespace Kitopia.DeviceCommunication;

public sealed class DeviceTransportService
{
    private readonly ILocalDataListener _localDataListener;
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;

    public DeviceTransportService(
        ILocalDataListener localDataListener,
        IDeviceDiscoveryService deviceDiscoveryService)
    {
        _localDataListener = localDataListener;
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
        var address = SelectTransportAddress(device);
        if (device.TcpPort <= 0 || address == IPAddress.None)
        {
            throw new InvalidOperationException("Invalid target address or port.");
        }

        var sender = new ProtocolSender((reader, token) =>
            _localDataListener.SendAsync(
                LocalDataTransportProtocol.Tcp,
                reader,
                new IPEndPoint(address, device.TcpPort),
                device.Id,
                token));

        return sender.SendAsync(envelope, payloadStream, progressCallback, cancellationToken);
    }

    private DiscoveredDevice ResolveDevice(string deviceId)
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

    private static IPAddress SelectTransportAddress(DiscoveredDevice device)
    {
        if (Socket.OSSupportsIPv6 && device.Ipv6Address != IPAddress.None)
        {
            return device.Ipv6Address;
        }

        return device.Ipv4Address;
    }
}
