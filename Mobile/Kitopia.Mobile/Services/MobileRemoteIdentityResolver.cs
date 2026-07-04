using System.Net;
using Kitopia.DeviceCommunication.Discovery;

namespace Kitopia.Mobile.Services;

public sealed class MobileRemoteIdentityResolver : Kitopia.DeviceCommunication.Transport.IRemoteIdentityResolver
{
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;

    public MobileRemoteIdentityResolver(IDeviceDiscoveryService deviceDiscoveryService)
    {
        _deviceDiscoveryService = deviceDiscoveryService;
    }

    public string? ResolveExpectedIdentityPublicKey(IPEndPoint remoteEndPoint)
    {
        var address = remoteEndPoint.Address.IsIPv4MappedToIPv6
            ? remoteEndPoint.Address.MapToIPv4()
            : remoteEndPoint.Address;
        var device = _deviceDiscoveryService.Devices.FirstOrDefault(item =>
            item.Ipv4Address.Equals(address) || item.Ipv6Address.Equals(address));
        return device?.Id;
    }
}
