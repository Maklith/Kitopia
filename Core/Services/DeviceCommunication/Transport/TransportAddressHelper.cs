using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Core.Services.DeviceCommunication.Transport;

internal static class TransportAddressHelper
{
    public static IPEndPoint CreateTargetEndPoint(IPAddress address, int port)
    {
        var normalizedAddress = NormalizeAddress(address);

        if (normalizedAddress.AddressFamily == AddressFamily.InterNetworkV6 &&
            normalizedAddress.IsIPv6LinkLocal &&
            normalizedAddress.ScopeId == 0)
        {
            normalizedAddress = TryAttachLocalScopeId(normalizedAddress);
        }

        return new IPEndPoint(normalizedAddress, port);
    }

    public static IPAddress NormalizeAddress(IPAddress address)
    {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    private static IPAddress TryAttachLocalScopeId(IPAddress address)
    {
        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                var ipProps = networkInterface.GetIPProperties();
                var ipv6Index = ipProps.GetIPv6Properties()?.Index;
                if (ipv6Index.HasValue && ipv6Index.Value > 0)
                {
                    return new IPAddress(address.GetAddressBytes(), ipv6Index.Value);
                }
            }
        }
        catch
        {
        }

        return address;
    }
}
