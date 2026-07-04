using System.IO.Pipelines;
using System.Net;
using Kitopia.DeviceCommunication.Protocol;
using Kitopia.DeviceCommunication.Security;

namespace Kitopia.DeviceCommunication.Transport;

public sealed class LocalDataListenerHost : IDisposable, ILocalDataListener
{
    private readonly object _sync = new();
    private readonly TcpLocalDataListener _tcpListener;
    private bool _isStarted;

    public LocalDataListenerHost(
        ProtocolSession protocolSession,
        DeviceTransportSecurity transportSecurity,
        IRemoteIdentityResolver remoteIdentityResolver)
    {
        _tcpListener = new TcpLocalDataListener(protocolSession, transportSecurity, remoteIdentityResolver);
    }

    public int TcpPort => _tcpListener.Port;

    public void Dispose()
    {
        StopListeningAsync().GetAwaiter().GetResult();
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

        var started = false;
        try
        {
            started = await _tcpListener.StartAsync(token);
        }
        catch
        {
            lock (_sync)
            {
                _isStarted = false;
            }

            throw;
        }

        if (started)
        {
            return;
        }

        lock (_sync)
        {
            _isStarted = false;
        }

        throw new InvalidOperationException("Failed to start TCP local data listener.");
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

        await _tcpListener.StopAsync().ConfigureAwait(false);
    }
}
