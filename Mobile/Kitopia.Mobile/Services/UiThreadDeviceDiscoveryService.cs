using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;
using Kitopia.DeviceCommunication.Discovery;
using ObservableCollections;

namespace Kitopia.Mobile.Services;

public sealed class UiThreadDeviceDiscoveryService : IDeviceDiscoveryService
{
    private readonly IDeviceDiscoveryService _innerService;
    private readonly ObservableList<DiscoveredDevice> _uiSource = [];
    private readonly ISynchronizedView<DiscoveredDevice, DiscoveredDevice> _uiView;
    private readonly Dictionary<DiscoveredDevice, DiscoveredDevice> _deviceMap = [];

    public UiThreadDeviceDiscoveryService(IDeviceDiscoveryService innerService)
    {
        _innerService = innerService;
        _uiView = _uiSource.CreateView(device => device);
        Devices = _uiView.ToNotifyCollectionChanged();

        foreach (var device in _innerService.Devices)
        {
            AddDevice(device);
        }

        ((INotifyCollectionChanged)_innerService.Devices).CollectionChanged += OnDevicesChanged;
    }

    public NotifyCollectionChangedSynchronizedViewList<DiscoveredDevice> Devices { get; }

    public Task StartAsync(CancellationToken token)
    {
        return _innerService.StartAsync(token);
    }

    public Task StopAsync()
    {
        return _innerService.StopAsync();
    }

    public void SetBackgroundMode(bool background)
    {
        _innerService.SetBackgroundMode(background);
    }

    public void Dispose()
    {
        ((INotifyCollectionChanged)_innerService.Devices).CollectionChanged -= OnDevicesChanged;

        foreach (var device in _deviceMap.Keys.ToArray())
        {
            device.PropertyChanged -= OnDevicePropertyChanged;
        }

        _deviceMap.Clear();
        _uiSource.Clear();
        Devices.Dispose();
        _uiView.Dispose();
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
                _uiSource.Clear();
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
        _uiSource.Add(mirroredDevice);
    }

    private void RemoveDevice(DiscoveredDevice device)
    {
        if (!_deviceMap.Remove(device, out var mirroredDevice))
        {
            return;
        }

        device.PropertyChanged -= OnDevicePropertyChanged;
        _uiSource.Remove(mirroredDevice);
    }

    private static void CopyDevice(DiscoveredDevice source, DiscoveredDevice destination)
    {
        destination.Id = source.Id;
        destination.Name = source.Name;
        destination.CustomName = source.CustomName;
        destination.Ipv4Address = source.Ipv4Address;
        destination.Ipv6Address = source.Ipv6Address;
        destination.TcpPort = source.TcpPort;
        destination.OperatingSystem = source.OperatingSystem;
        destination.LastSeen = source.LastSeen;
    }
}
