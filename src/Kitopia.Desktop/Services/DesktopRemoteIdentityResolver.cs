using System;
using System.Linq;
using System.Net;
using Kitopia.Feature.DeviceCommunication.Discovery;
using Kitopia.Feature.DeviceCommunication.Transport;

namespace Kitopia.Desktop.Services;

public sealed class DesktopRemoteIdentityResolver : IRemoteIdentityResolver
{
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;

    public DesktopRemoteIdentityResolver(IDeviceDiscoveryService deviceDiscoveryService)
    {
        _deviceDiscoveryService = deviceDiscoveryService;
    }

    public string? ResolveExpectedIdentityPublicKey(IPEndPoint remoteEndPoint)
    {
        ArgumentNullException.ThrowIfNull(remoteEndPoint);

        var remoteAddress = NormalizeAddress(remoteEndPoint.Address);
        return _deviceDiscoveryService.Devices
            .FirstOrDefault(device =>
                NormalizeAddress(device.Ipv4Address).Equals(remoteAddress) ||
                NormalizeAddress(device.Ipv6Address).Equals(remoteAddress))
            ?.Id;
    }

    private static IPAddress NormalizeAddress(IPAddress address)
    {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }
}
