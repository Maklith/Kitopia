using System.Net;

namespace Core.Services.DeviceCommunication;

public interface ILocalDataBusService : IDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();

    IDisposable Subscribe<TMessage>(EventHandler<LocalDataBusMessageReceivedEventArgs<TMessage>> handler);

    Task PublishAsync<TMessage>(
        LocalDataBusSendContext sendContext,
        TMessage message,
        CancellationToken cancellationToken = default);
}

public readonly record struct LocalDataBusSendContext(
    ILocalDataListener Listener,
    LocalDataTransportProtocol Protocol,
    IPEndPoint RemoteEndPoint,
    string RemoteIdentityPublicKey);
