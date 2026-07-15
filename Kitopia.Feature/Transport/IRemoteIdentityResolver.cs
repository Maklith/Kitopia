using System.Net;

namespace Kitopia.Feature.DeviceCommunication.Transport;

public interface IRemoteIdentityResolver
{
    string? ResolveExpectedIdentityPublicKey(IPEndPoint remoteEndPoint);
}
