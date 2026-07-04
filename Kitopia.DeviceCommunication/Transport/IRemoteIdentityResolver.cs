using System.Net;

namespace Kitopia.DeviceCommunication.Transport;

public interface IRemoteIdentityResolver
{
    string? ResolveExpectedIdentityPublicKey(IPEndPoint remoteEndPoint);
}
