// Author: liaom
// SolutionName: Kitopia
// ProjectName: Core
// FileName:ILocalDataListener.cs
// Date: 2026/04/11 11:04
// FileEffect:

using System.IO.Pipelines;
using System.Net;

namespace Core.Services.DeviceCommunication;

public interface ILocalDataListener {
    public int TcpPort { get; }
    public int QuicPort { get; }
    public bool SupportsQuic { get; }
    public Task StartListeningAsync(CancellationToken token=default);
    public Task StopListeningAsync();
    public Task SendAsync(
        LocalDataTransportProtocol protocol,
        ReadOnlyMemory<byte> payload,
        IPEndPoint remoteEndPoint,
        string? remoteIdentityPublicKey = null,
        CancellationToken token = default);
    public Task SendAsync(
        LocalDataTransportProtocol protocol,
        PipeReader payloadReader,
        IPEndPoint remoteEndPoint,
        string? remoteIdentityPublicKey = null,
        CancellationToken token = default);
    public Task SendAsync(
        LocalDataTransportProtocol protocol,
        Stream stream,
        IPEndPoint remoteEndPoint,
        string? remoteIdentityPublicKey = null,
        CancellationToken token = default);
}
