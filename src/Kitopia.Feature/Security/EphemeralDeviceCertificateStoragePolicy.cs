using System.Security.Cryptography.X509Certificates;

namespace Kitopia.Feature.DeviceCommunication.Security;

public sealed class EphemeralDeviceCertificateStoragePolicy : IDeviceCertificateStoragePolicy
{
    public static EphemeralDeviceCertificateStoragePolicy Instance { get; } = new();

    private EphemeralDeviceCertificateStoragePolicy()
    {
    }

    public X509KeyStorageFlags KeyStorageFlags =>
        X509KeyStorageFlags.EphemeralKeySet |
        X509KeyStorageFlags.Exportable;
}
