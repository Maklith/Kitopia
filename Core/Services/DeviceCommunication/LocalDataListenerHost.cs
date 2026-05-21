using System.IO.Pipelines;
using System.Net;
using System.Net.Quic;
using Core.Services.Config;
using Core.Services.DeviceCommunication.Security;
using Serilog;

namespace Core.Services.DeviceCommunication;

public sealed class LocalDataListenerHost : IDisposable, ILocalDataListener
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<LocalDataListenerHost>();

    private readonly object _sync = new();
    private readonly TcpLocalDataListener _tcpListener;
    private readonly QuicLocalDataListener _quicListener;

    private bool _isStarted;

    public LocalDataListenerHost(Protocol.ProtocolSession protocolSession, DeviceTransportSecurity transportSecurity)
    {
        _tcpListener = new TcpLocalDataListener(protocolSession, transportSecurity);
        _quicListener = new QuicLocalDataListener(protocolSession, transportSecurity);
    }

    public int TcpPort => _tcpListener.Port;

    public int QuicPort => _quicListener.Port;

    public bool SupportsQuic => _quicListener.IsRunning;

    public void Dispose()
    {
        StopListeningAsync().GetAwaiter().GetResult();
        _quicListener.Dispose();
        _tcpListener.Dispose();
    }

    public async Task StartListeningAsync(CancellationToken token = default)
    {
        lock (_sync)
        {
            if (_isStarted)
            {
                return;
            }

            _isStarted = true;
        }

        await _tcpListener.StartAsync(token);

        if (!ConfigManger.Config.deviceCommunicationEnableQuic)
        {
            Logger.Information("QUIC local listener disabled by configuration.");
            return;
        }

        if (QuicConnection.IsSupported && QuicListener.IsSupported)
        {
            await _quicListener.StartAsync(token);
            return;
        }

        Logger.Information(
            "QUIC local listener skipped. QuicConnectionSupported={ConnectionSupported}, QuicListenerSupported={ListenerSupported}",
            QuicConnection.IsSupported,
            QuicListener.IsSupported);
    }

    public async Task SendAsync(
        LocalDataTransportProtocol protocol,
        PipeReader payloadReader,
        IPEndPoint remoteEndPoint,
        string? remoteIdentityPublicKey = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(payloadReader);
        ArgumentNullException.ThrowIfNull(remoteEndPoint);
        if (string.IsNullOrWhiteSpace(remoteIdentityPublicKey))
        {
            throw new ArgumentException("Remote identity public key is required.", nameof(remoteIdentityPublicKey));
        }

        switch (protocol)
        {
            case LocalDataTransportProtocol.Tcp:
                await _tcpListener.SendAsync(payloadReader, remoteEndPoint, remoteIdentityPublicKey, token);
                break;
            case LocalDataTransportProtocol.Quic:
                await _quicListener.SendAsync(payloadReader, remoteEndPoint, remoteIdentityPublicKey, token);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(protocol), protocol, "Unsupported transport protocol.");
        }
    }

    public async Task StopListeningAsync()
    {
        bool shouldStop;

        lock (_sync)
        {
            shouldStop = _isStarted;
            _isStarted = false;
        }

        if (!shouldStop)
        {
            return;
        }

        await _quicListener.StopAsync().ConfigureAwait(false);
        await _tcpListener.StopAsync().ConfigureAwait(false);
    }

}
