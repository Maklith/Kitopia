using ObservableCollections;

namespace Kitopia.Feature.DeviceCommunication.Discovery;

public interface IDeviceDiscoveryService : IDisposable
{
    NotifyCollectionChangedSynchronizedViewList<DiscoveredDevice> Devices { get; }
    Task StartAsync(CancellationToken token);
    Task StopAsync();
    void SetBackgroundMode(bool background) { }
}
