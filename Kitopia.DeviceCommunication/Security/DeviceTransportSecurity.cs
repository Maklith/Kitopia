using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Kitopia.DeviceCommunication.Diagnostics;
using Kitopia.DeviceCommunication.Identity;

namespace Kitopia.DeviceCommunication.Security;

public sealed class DeviceTransportSecurity
{
    private const string LogCategory = "DeviceTransportSecurity";
    private readonly IDeviceIdentityStore _deviceIdentityStore;

    public DeviceTransportSecurity(IDeviceIdentityStore deviceIdentityStore)
    {
        _deviceIdentityStore = deviceIdentityStore;
    }

    public X509Certificate2 CreateIdentityCertificate(string subjectName)
    {
        if (!_deviceIdentityStore.TryGetIdentity(out var identity) ||
            string.IsNullOrWhiteSpace(identity.PrivateKey))
        {
            throw new InvalidOperationException("Device identity private key is not initialized.");
        }

        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(identity.PrivateKey), out _);
        var request = new CertificateRequest(
            subjectName,
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                false));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                [
                    new Oid("1.3.6.1.5.5.7.3.1"),
                    new Oid("1.3.6.1.5.5.7.3.2")
                ],
                false));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var keyStorageFlags = ResolvePkcs12KeyStorageFlags();
        return X509CertificateLoader.LoadPkcs12(
            certificate.Export(X509ContentType.Pfx),
            password: null,
            keyStorageFlags);
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

    private static X509KeyStorageFlags ResolvePkcs12KeyStorageFlags()
    {
        if (OperatingSystem.IsAndroid() ||
            OperatingSystem.IsIOS() ||
            OperatingSystem.IsTvOS() ||
            OperatingSystem.IsMacCatalyst())
        {
            DeviceCommunicationDiagnostics.Info(
                LogCategory,
                "Using ephemeral PKCS#12 key storage flags for mobile platform compatibility.");
            return X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable;
        }

        return X509KeyStorageFlags.UserKeySet |
               X509KeyStorageFlags.PersistKeySet |
               X509KeyStorageFlags.Exportable;
    }
}
