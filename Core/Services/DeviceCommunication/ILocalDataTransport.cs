using System.Net;
using System.IO.Pipelines;

namespace Core.Services.DeviceCommunication;

public enum LocalDataTransportProtocol
{
    Tcp = 1,
    Quic = 2
}

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
