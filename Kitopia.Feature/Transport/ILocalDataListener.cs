using System.IO.Pipelines;
using System.Net;

namespace Kitopia.Feature.DeviceCommunication.Transport;

public interface ILocalDataListener
{
    int TcpPort { get; }
    Task StartListeningAsync(CancellationToken token = default);
    Task StopListeningAsync();
    Task SendAsync(
        LocalDataTransportProtocol protocol,
        PipeReader payloadReader,
        IPEndPoint remoteEndPoint,
        string? remoteIdentityPublicKey = null,
        CancellationToken token = default);
}
