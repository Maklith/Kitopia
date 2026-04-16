using System;
using ObservableCollections;
using PluginCore;

namespace Core.Services.DeviceCommunication.Discovery;

public interface IDeviceDiscoveryService : IDisposable
{
    NotifyCollectionChangedSynchronizedViewList<DeviceModel> Devices { get; }
    
    Task StartAsync(CancellationToken token);
    Task StopAsync();
}
