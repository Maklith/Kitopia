using System.Security.Cryptography.X509Certificates;

namespace Kitopia.Feature.DeviceCommunication.Security;

public interface IDeviceCertificateStoragePolicy
{
    X509KeyStorageFlags KeyStorageFlags { get; }
}
