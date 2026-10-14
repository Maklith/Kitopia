using System.Security.Cryptography.X509Certificates;

namespace Kitopia.Feature.DeviceCommunication.Security;

public sealed class PersistedUserDeviceCertificateStoragePolicy : IDeviceCertificateStoragePolicy
{
    public static PersistedUserDeviceCertificateStoragePolicy Instance { get; } = new();

    private PersistedUserDeviceCertificateStoragePolicy()
    {
    }

    public X509KeyStorageFlags KeyStorageFlags =>
        X509KeyStorageFlags.UserKeySet |
        X509KeyStorageFlags.PersistKeySet |
        X509KeyStorageFlags.Exportable;
}
