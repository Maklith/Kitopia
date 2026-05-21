using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Core.Services.Config;
using Core.Services.DeviceCommunication.Discovery;

namespace Core.Services.DeviceCommunication.Security;

public sealed class DeviceTransportSecurity
{
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;

    public DeviceTransportSecurity(IDeviceDiscoveryService deviceDiscoveryService)
    {
        _deviceDiscoveryService = deviceDiscoveryService;
    }

    public X509Certificate2 CreateIdentityCertificate(string subjectName)
    {
        var privateKey = ConfigManger.Config.devicePrivateKey?.Trim();
        if (string.IsNullOrWhiteSpace(privateKey))
        {
            throw new InvalidOperationException("Device identity private key is not initialized.");
        }

        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKey), out _);
        var request = new CertificateRequest(
            subjectName,
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                [new Oid("1.3.6.1.5.5.7.3.1")],
                false));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return new X509Certificate2(certificate.Export(X509ContentType.Pfx));
    }

    public string? ResolveExpectedIdentityPublicKey(IPEndPoint? remoteEndPoint)
    {
        if (remoteEndPoint is null)
        {
            return null;
        }

        var remoteAddress = NormalizeAddress(remoteEndPoint.Address);
        var matchedDevice = _deviceDiscoveryService.Devices.FirstOrDefault(device =>
            NormalizeAddress(device.Ipv4Address).Equals(remoteAddress) ||
            NormalizeAddress(device.Ipv6Address).Equals(remoteAddress));

        return matchedDevice is null || string.IsNullOrWhiteSpace(matchedDevice.Id)
            ? null
            : matchedDevice.Id;
    }

    public bool ValidateRemoteCertificate(X509Certificate? certificate, string? expectedIdentityPublicKey)
    {
        if (certificate is null || string.IsNullOrWhiteSpace(expectedIdentityPublicKey))
        {
            return false;
        }

        if (!TryGetCertificateIdentityPublicKey(certificate, out var certificateIdentityPublicKey))
        {
            return false;
        }

        return string.Equals(certificateIdentityPublicKey, expectedIdentityPublicKey.Trim(), StringComparison.Ordinal);
    }

    public static IPAddress NormalizeAddress(IPAddress address)
    {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    private static bool TryGetCertificateIdentityPublicKey(X509Certificate certificate, out string publicKey)
    {
        publicKey = string.Empty;
        try
        {
            using var certificate2 = certificate as X509Certificate2 ?? new X509Certificate2(certificate);
            using var rsa = certificate2.GetRSAPublicKey();
            if (rsa is null)
            {
                return false;
            }

            publicKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
            return true;
        }
        catch
        {
            return false;
        }
    }
}
