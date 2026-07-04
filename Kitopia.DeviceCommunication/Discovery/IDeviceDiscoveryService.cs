using System.Collections.ObjectModel;

namespace Kitopia.DeviceCommunication.Discovery;

public interface IDeviceDiscoveryService : IDisposable
{
    ObservableCollection<DiscoveredDevice> Devices { get; }
    Task StartAsync(CancellationToken token);
    Task StopAsync();
}
