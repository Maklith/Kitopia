using System.Net;
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
    public event LocalDataPacketReceivedHandler? PacketReceived;

    public LocalDataListenerHost()
    {
        _udpListener = new UdpLocalDataListener();
        _quicListener = new QuicLocalDataListener();
        _udpListener.PacketReceived += OnPacketReceivedAsync;
        _quicListener.PacketReceived += OnPacketReceivedAsync;
    }

    public int UdpPort => _udpListener.Port;

    public int QuicPort => _quicListener.Port;

    public bool SupportsQuic => _quicListener.IsRunning;
    
    
    public void Dispose()
    {
        StopListeningAsync().GetAwaiter().GetResult();
        _udpListener.PacketReceived -= OnPacketReceivedAsync;
        _quicListener.PacketReceived -= OnPacketReceivedAsync;
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

        await _udpListener.StartAsync(token);

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

    public Task SendAsync(
        LocalDataTransportProtocol protocol,
        ReadOnlyMemory<byte> payload,
        IPEndPoint remoteEndPoint,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(remoteEndPoint);
        return protocol switch
        {
            LocalDataTransportProtocol.Udp => _udpListener.SendAsync(payload, remoteEndPoint, token),
            LocalDataTransportProtocol.Quic => _quicListener.SendAsync(payload, remoteEndPoint, token),
            _ => throw new ArgumentOutOfRangeException(nameof(protocol), protocol, "Unsupported transport protocol.")
        };
    }

    public async Task SendAsync(
        LocalDataTransportProtocol protocol,
        Stream stream,
        IPEndPoint remoteEndPoint,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, token);
        await SendAsync(protocol, memory.ToArray(), remoteEndPoint, token);
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

    private async ValueTask OnPacketReceivedAsync(LocalDataPacket packet, CancellationToken token)
    {
        var handler = PacketReceived;
        if (handler is null)
        {
            return;
        }

        foreach (LocalDataPacketReceivedHandler subscriber in handler.GetInvocationList())
        {
            try
            {
                await subscriber(packet, token);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Local data listener host packet callback failed");
            }
        }
    }
}
