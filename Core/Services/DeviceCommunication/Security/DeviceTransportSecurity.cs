using System.Net;
using System.Security.Cryptography.X509Certificates;
using Core.Services.DeviceCommunication.Discovery;
using Kitopia.DeviceCommunication.Identity;
using SharedDeviceTransportSecurity = Kitopia.DeviceCommunication.Security.DeviceTransportSecurity;

namespace Core.Services.DeviceCommunication.Security;

public sealed class DeviceTransportSecurity
{
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;
    private readonly SharedDeviceTransportSecurity _sharedSecurity;

    public DeviceTransportSecurity(
        IDeviceDiscoveryService deviceDiscoveryService,
        IDeviceIdentityStore deviceIdentityStore)
    {
        _deviceDiscoveryService = deviceDiscoveryService;
        _sharedSecurity = new SharedDeviceTransportSecurity(deviceIdentityStore);
    }

    public X509Certificate2 CreateIdentityCertificate(string subjectName)
    {
        return _sharedSecurity.CreateIdentityCertificate(subjectName);
    }

    public string? ResolveExpectedIdentityPublicKey(IPEndPoint? remoteEndPoint)
    {
        if (remoteEndPoint is null)
        {
            return null;
        }

        var remoteAddress = NormalizeAddressForComparison(remoteEndPoint.Address);
        var matchedDevice = _deviceDiscoveryService.Devices.FirstOrDefault(device =>
            AreEquivalentAddresses(device.Ipv4Address, remoteAddress) ||
            AreEquivalentAddresses(device.Ipv6Address, remoteAddress));

        return matchedDevice is null || string.IsNullOrWhiteSpace(matchedDevice.Id)
            ? null
            : matchedDevice.Id;
    }

    public bool ValidateRemoteCertificate(X509Certificate? certificate, string? expectedIdentityPublicKey)
    {
        return _sharedSecurity.ValidateRemoteCertificate(certificate, expectedIdentityPublicKey);
    }

    public bool ValidateIncomingRemoteCertificate(X509Certificate? certificate, IPEndPoint? remoteEndPoint)
    {
        if (certificate is null)
        {
            return false;
        }

        var expectedIdentityPublicKey = ResolveExpectedIdentityPublicKey(remoteEndPoint);
        if (!string.IsNullOrWhiteSpace(expectedIdentityPublicKey) &&
            _sharedSecurity.ValidateRemoteCertificate(certificate, expectedIdentityPublicKey))
        {
            return true;
        }

        var candidateDevices = remoteEndPoint is null
            ? _deviceDiscoveryService.Devices
            : _deviceDiscoveryService.Devices.Where(device =>
                AreEquivalentAddresses(device.Ipv4Address, remoteEndPoint.Address) ||
                AreEquivalentAddresses(device.Ipv6Address, remoteEndPoint.Address));

        foreach (var device in candidateDevices)
        {
            if (!string.IsNullOrWhiteSpace(device.Id) &&
                _sharedSecurity.ValidateRemoteCertificate(certificate, device.Id))
            {
                return true;
            }
        }

        foreach (var device in _deviceDiscoveryService.Devices)
        {
            if (!string.IsNullOrWhiteSpace(device.Id) &&
                _sharedSecurity.ValidateRemoteCertificate(certificate, device.Id))
            {
                return true;
            }
        }

        return false;
    }

    public static IPAddress NormalizeAddress(IPAddress address)
    {
        return SharedDeviceTransportSecurity.NormalizeAddress(address);
    }

    private static bool AreEquivalentAddresses(IPAddress left, IPAddress right)
    {
        var normalizedLeft = NormalizeAddressForComparison(left);
        var normalizedRight = NormalizeAddressForComparison(right);

        if (normalizedLeft.AddressFamily != normalizedRight.AddressFamily)
        {
            return false;
        }

        if (normalizedLeft.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return normalizedLeft.GetAddressBytes().SequenceEqual(normalizedRight.GetAddressBytes());
        }

        return normalizedLeft.Equals(normalizedRight);
    }

    private static IPAddress NormalizeAddressForComparison(IPAddress address)
    {
        var normalized = NormalizeAddress(address);
        return normalized.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? new IPAddress(normalized.GetAddressBytes())
            : normalized;
    }
}
