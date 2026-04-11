using System.Net;

namespace Core.Services.DeviceCommunication;

public enum LocalDataTransportProtocol
{
    Udp = 1,
    Quic = 2
}

public sealed record LocalDataPacket(
    LocalDataTransportProtocol Protocol,
    IPEndPoint RemoteEndPoint,
    byte[] Payload);

public delegate ValueTask LocalDataPacketReceivedHandler(LocalDataPacket packet, CancellationToken cancellationToken);

public interface ILocalDataTransport : IDisposable
{
    int Port { get; }
    bool IsRunning { get; }
    LocalDataTransportProtocol Protocol { get; }
    event LocalDataPacketReceivedHandler? PacketReceived;

    Task<bool> StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
    Task SendAsync(
        ReadOnlyMemory<byte> payload,
        IPEndPoint remoteEndPoint,
        string? remoteIdentityPublicKey = null,
        CancellationToken cancellationToken = default);
}
