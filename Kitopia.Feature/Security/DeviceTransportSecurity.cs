using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Kitopia.Feature.DeviceCommunication.Identity;

namespace Kitopia.Feature.DeviceCommunication.Security;

public sealed class DeviceTransportSecurity
{
    private readonly IDeviceIdentityStore _deviceIdentityStore;
    private readonly IDeviceCertificateStoragePolicy _certificateStoragePolicy;

    public DeviceTransportSecurity(
        IDeviceIdentityStore deviceIdentityStore,
        IDeviceCertificateStoragePolicy? certificateStoragePolicy = null)
    {
        _deviceIdentityStore = deviceIdentityStore;
        _certificateStoragePolicy = certificateStoragePolicy ??
                                    PersistedUserDeviceCertificateStoragePolicy.Instance;
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
        return X509CertificateLoader.LoadPkcs12(
            certificate.Export(X509ContentType.Pfx),
            password: null,
            _certificateStoragePolicy.KeyStorageFlags);
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
        X509Certificate2? ownedCertificate = null;
        try
        {
            var certificate2 = certificate as X509Certificate2;
            if (certificate2 is null)
            {
                ownedCertificate = new X509Certificate2(certificate);
                certificate2 = ownedCertificate;
            }

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
        finally
        {
            ownedCertificate?.Dispose();
        }
    }
}
