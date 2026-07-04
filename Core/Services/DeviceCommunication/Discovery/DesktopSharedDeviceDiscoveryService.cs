using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Kitopia.DeviceCommunication.Discovery;
using PluginCore;
using CoreDiscoveryService = Core.Services.DeviceCommunication.Discovery.IDeviceDiscoveryService;

namespace Core.Services.DeviceCommunication.Discovery;

public sealed class DesktopSharedDeviceDiscoveryService : Kitopia.DeviceCommunication.Discovery.IDeviceDiscoveryService
{
    private readonly CoreDiscoveryService _coreService;
    private readonly Dictionary<DeviceModel, DiscoveredDevice> _deviceMap = [];

    public DesktopSharedDeviceDiscoveryService(CoreDiscoveryService coreService)
    {
        _coreService = coreService;
        Devices = [];

        foreach (var device in _coreService.Devices)
        {
            AddDevice(device);
        }

        ((INotifyCollectionChanged)_coreService.Devices).CollectionChanged += OnDevicesChanged;
    }

    public ObservableCollection<DiscoveredDevice> Devices { get; }

    public Task StartAsync(CancellationToken token)
    {
        return _coreService.StartAsync(token);
    }

    public Task StopAsync()
    {
        return _coreService.StopAsync();
    }

    public void Dispose()
    {
        ((INotifyCollectionChanged)_coreService.Devices).CollectionChanged -= OnDevicesChanged;

        foreach (var device in _deviceMap.Keys.ToArray())
        {
            device.PropertyChanged -= OnDevicePropertyChanged;
        }

        _deviceMap.Clear();
        Devices.Clear();
    }

    private void OnDevicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Reset)
        {
            foreach (var device in _deviceMap.Keys.ToArray())
            {
                device.PropertyChanged -= OnDevicePropertyChanged;
            }

            _deviceMap.Clear();
            Devices.Clear();
            foreach (var device in _coreService.Devices)
            {
                AddDevice(device);
            }

            return;
        }

        if (e.OldItems is not null)
        {
            foreach (DeviceModel device in e.OldItems)
            {
                RemoveDevice(device);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (DeviceModel device in e.NewItems)
            {
                AddDevice(device);
            }
        }
    }

    private void OnDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DeviceModel device || !_deviceMap.TryGetValue(device, out var sharedDevice))
        {
            return;
        }

        CopyDevice(device, sharedDevice);
    }

    private void AddDevice(DeviceModel device)
    {
        if (_deviceMap.ContainsKey(device))
        {
            return;
        }

        var sharedDevice = new DiscoveredDevice();
        CopyDevice(device, sharedDevice);
        _deviceMap[device] = sharedDevice;
        device.PropertyChanged += OnDevicePropertyChanged;
        Devices.Add(sharedDevice);
    }

    private void RemoveDevice(DeviceModel device)
    {
        if (!_deviceMap.Remove(device, out var sharedDevice))
        {
            return;
        }

        device.PropertyChanged -= OnDevicePropertyChanged;
        Devices.Remove(sharedDevice);
    }

    private static void CopyDevice(DeviceModel source, DiscoveredDevice destination)
    {
        destination.Id = source.Id;
        destination.Name = source.Name;
        destination.CustomName = source.CustomName;
        destination.Ipv4Address = source.Ipv4Address;
        destination.Ipv6Address = source.Ipv6Address;
        destination.TcpPort = source.TcpPort;
        destination.LastSeen = source.LastSeen;
    }
}
