using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Core.Services.DeviceCommunication.Protocol;
using PluginCore;

namespace Core.Services.DeviceCommunication.Transport;

public sealed class TransportService : ITransportService
{
    private readonly QuicTransport _quicTransport;
    private readonly UdpFallbackTransport _udpTransport;
    private readonly object _lifecycleLock = new();

    private CancellationTokenSource? _cts;

    public TransportService()
    {
        _quicTransport = new QuicTransport();
        _udpTransport = new UdpFallbackTransport();
        _quicTransport.PacketReceived += OnPacketReceived;
        _udpTransport.PacketReceived += OnPacketReceived;
    }

    public event EventHandler<TransportPacketReceivedEventArgs>? PacketReceived;

    public int AdvertisedPort { get; private set; }
    public bool SupportsQuic { get; private set; }

    public Task StartAsync(CancellationToken ct)
    {
        lock (_lifecycleLock)
        {
            StopCore();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = _cts.Token;

            SupportsQuic = _quicTransport.Start(token);
            _udpTransport.Start(SupportsQuic, _quicTransport.Port, token);

            AdvertisedPort = SupportsQuic
                ? _quicTransport.Port
                : Math.Max(0, _udpTransport.DataPort - 1);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        lock (_lifecycleLock)
        {
            StopCore();
        }

        return Task.CompletedTask;
    }

    public async Task SendAsync(
        DeviceModel target,
        PacketMetadata packet,
        Stream payload,
        Action<long>? onProgress = null)
    {
        var sourceStream = payload;
        MemoryStream? bufferedPayload = null;

        if (!payload.CanSeek && SupportsQuic)
        {
            bufferedPayload = new MemoryStream();
            await payload.CopyToAsync(bufferedPayload);
            bufferedPayload.Position = 0;
            sourceStream = bufferedPayload;
        }

        try
        {
            if (SupportsQuic && target.Port > 0)
            {
                if (sourceStream.CanSeek)
                {
                    sourceStream.Position = 0;
                }

                var quicSent = await _quicTransport.TrySendAsync(target, packet, sourceStream, onProgress);
                if (quicSent)
                {
                    return;
                }
            }

            if (sourceStream.CanSeek)
            {
                sourceStream.Position = 0;
            }

            await _udpTransport.SendAsync(target, packet, sourceStream, onProgress);
        }
        finally
        {
            bufferedPayload?.Dispose();
        }
    }

    public void Dispose()
    {
        _quicTransport.PacketReceived -= OnPacketReceived;
        _udpTransport.PacketReceived -= OnPacketReceived;
        StopCore();
        _quicTransport.Dispose();
        _udpTransport.Dispose();
    }

    private void OnPacketReceived(object? sender, TransportPacketReceivedEventArgs e)
    {
        PacketReceived?.Invoke(this, e);
    }

    private void StopCore()
    {
        var cts = _cts;
        _cts = null;
        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch
            {
            }

            cts.Dispose();
        }

        try
        {
            _quicTransport.StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }

        _udpTransport.Stop();
        AdvertisedPort = 0;
        SupportsQuic = false;
    }
}
