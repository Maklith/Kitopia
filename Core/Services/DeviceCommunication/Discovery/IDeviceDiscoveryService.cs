using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using PluginCore;

namespace Core.Services.DeviceCommunication.Discovery;

public interface IDeviceDiscoveryService : IDisposable
{
    ObservableCollection<DeviceModel> Devices { get; }

    event EventHandler<DeviceDiscoveryEventArgs>? DeviceDiscovered;
    event EventHandler<DeviceDiscoveryEventArgs>? DeviceUpdated;
    event EventHandler<DeviceDiscoveryEventArgs>? DeviceLost;

    void Start(DiscoveryAnnouncement announcement);
    void Stop();
}
