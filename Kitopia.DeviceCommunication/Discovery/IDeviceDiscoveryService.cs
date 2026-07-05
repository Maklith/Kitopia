using ObservableCollections;

namespace Kitopia.DeviceCommunication.Discovery;

public interface IDeviceDiscoveryService : IDisposable
{
    NotifyCollectionChangedSynchronizedViewList<DiscoveredDevice> Devices { get; }
    Task StartAsync(CancellationToken token);
    Task StopAsync();
}
