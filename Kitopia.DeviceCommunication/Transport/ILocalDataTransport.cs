using System.IO.Pipelines;
using System.Net;

namespace Kitopia.DeviceCommunication.Transport;

public interface ILocalDataTransport : IDisposable
{
    int Port { get; }
    bool IsRunning { get; }
    LocalDataTransportProtocol Protocol { get; }

    Task<bool> StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
    Task SendAsync(
        ReadOnlyMemory<byte> payload,
        IPEndPoint remoteEndPoint,
        string? remoteIdentityPublicKey = null,
        CancellationToken cancellationToken = default);
    Task SendAsync(
        PipeReader payloadReader,
        IPEndPoint remoteEndPoint,
        string? remoteIdentityPublicKey = null,
        CancellationToken cancellationToken = default);
}
