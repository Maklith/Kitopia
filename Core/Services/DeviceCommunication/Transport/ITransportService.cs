using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Core.Services.DeviceCommunication.Protocol;
using PluginCore;

namespace Core.Services.DeviceCommunication.Transport;

public interface ITransportService : IDisposable
{
    event EventHandler<TransportPacketReceivedEventArgs>? PacketReceived;

    int AdvertisedPort { get; }
    bool SupportsQuic { get; }

    Task StartAsync(CancellationToken ct);
    Task StopAsync();
    Task SendAsync(DeviceModel target, PacketMetadata packet, Stream payload, Action<long>? onProgress = null);
}
