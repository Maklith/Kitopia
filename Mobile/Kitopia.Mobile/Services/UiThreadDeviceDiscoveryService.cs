using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;
using Kitopia.DeviceCommunication.Discovery;

namespace Kitopia.Mobile.Services;

public sealed class UiThreadDeviceDiscoveryService : IDeviceDiscoveryService
{
    private readonly IDeviceDiscoveryService _innerService;
    private readonly Dictionary<DiscoveredDevice, DiscoveredDevice> _deviceMap = [];

    public UiThreadDeviceDiscoveryService(IDeviceDiscoveryService innerService)
    {
        _innerService = innerService;
        Devices = [];

        foreach (var device in _innerService.Devices)
        {
            AddDevice(device);
        }

        ((INotifyCollectionChanged)_innerService.Devices).CollectionChanged += OnDevicesChanged;
    }

    public ObservableCollection<DiscoveredDevice> Devices { get; }

    public Task StartAsync(CancellationToken token)
    {
        return _innerService.StartAsync(token);
    }

    public Task StopAsync()
    {
        return _innerService.StopAsync();
    }

    public void Dispose()
    {
        ((INotifyCollectionChanged)_innerService.Devices).CollectionChanged -= OnDevicesChanged;

        foreach (var device in _deviceMap.Keys.ToArray())
        {
            device.PropertyChanged -= OnDevicePropertyChanged;
        }

        _deviceMap.Clear();
        Devices.Clear();
        _innerService.Dispose();
    }

    private void OnDevicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _ = sender;
        Dispatcher.UIThread.Post(() =>
        {
            if (e.Action is NotifyCollectionChangedAction.Reset)
            {
                foreach (var device in _deviceMap.Keys.ToArray())
                {
                    device.PropertyChanged -= OnDevicePropertyChanged;
                }

                _deviceMap.Clear();
                Devices.Clear();
                foreach (var device in _innerService.Devices)
                {
                    AddDevice(device);
                }

                return;
            }

            if (e.OldItems is not null)
            {
                foreach (DiscoveredDevice device in e.OldItems)
                {
                    RemoveDevice(device);
                }
            }

            if (e.NewItems is not null)
            {
                foreach (DiscoveredDevice device in e.NewItems)
                {
                    AddDevice(device);
                }
            }
        });
    }

    private void OnDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = e;
        if (sender is not DiscoveredDevice device || !_deviceMap.TryGetValue(device, out var mirroredDevice))
        {
            return;
        }

        Dispatcher.UIThread.Post(() => CopyDevice(device, mirroredDevice));
    }

    private void AddDevice(DiscoveredDevice device)
    {
        if (_deviceMap.ContainsKey(device))
        {
            return;
        }

        var mirroredDevice = new DiscoveredDevice();
        CopyDevice(device, mirroredDevice);
        _deviceMap[device] = mirroredDevice;
        device.PropertyChanged += OnDevicePropertyChanged;
        Devices.Add(mirroredDevice);
    }

    private void RemoveDevice(DiscoveredDevice device)
    {
        if (!_deviceMap.Remove(device, out var mirroredDevice))
        {
            return;
        }

        device.PropertyChanged -= OnDevicePropertyChanged;
        Devices.Remove(mirroredDevice);
    }

    private static void CopyDevice(DiscoveredDevice source, DiscoveredDevice destination)
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
