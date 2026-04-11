using System.Net.Quic;
using Core.Services;
using Serilog;

namespace Core.Services.DeviceCommunication;

public sealed class LocalDataListenerHost : IDisposable, ILocalDataListener
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<LocalDataListenerHost>();

    private readonly object _sync = new();
    private readonly UdpLocalDataListener _udpListener;
    private readonly QuicLocalDataListener _quicListener;

    private bool _isStarted;
    private bool _supportsQuic;

    public LocalDataListenerHost()
    {
        _udpListener = new UdpLocalDataListener();
        _quicListener = new QuicLocalDataListener();
        
    }

    public int UdpPort => _udpListener.Port;

    public int QuicPort => _quicListener.Port;

    public bool SupportsQuic => _quicListener.IsRunning;
    
    
    public void Dispose()
    {
        StopListeningAsync().GetAwaiter().GetResult();
        _quicListener.Dispose();
        _udpListener.Dispose();
    }

    public async Task StartListeningAsync(CancellationToken token= default) {
        lock (_sync)
        {
            if (_isStarted)
            {
                return;
            }

            _isStarted = true;
        }

        _udpListener.Start();

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
    public async Task StopListeningAsync() {
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

        await _quicListener.StopAsync();
        await _udpListener.StopAsync();
    }
}
