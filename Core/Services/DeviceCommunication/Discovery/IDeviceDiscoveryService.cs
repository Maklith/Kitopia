using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using PluginCore;

namespace Core.Services.DeviceCommunication.Discovery;

public interface IDeviceDiscoveryService : IDisposable
{
    ObservableCollection<DeviceModel> Devices { get; }
    
    Task StartAsync(CancellationToken token);
    Task StopAsync();
}
