using CommunityToolkit.Mvvm.ComponentModel;
using Kitopia.DeviceCommunication.Discovery;
using ObservableCollections;

namespace Kitopia.Mobile.ViewModels;

public sealed partial class DeviceListViewModel : ObservableObject
{
    public DeviceListViewModel(IDeviceDiscoveryService discoveryService)
    {
        _discoveryService = discoveryService;
    }

    private readonly IDeviceDiscoveryService _discoveryService;

    public NotifyCollectionChangedSynchronizedViewList<DiscoveredDevice> Devices => _discoveryService.Devices;

    [ObservableProperty]
    private DiscoveredDevice? _selectedDevice;

    public event Action<DiscoveredDevice?>? SelectedDeviceChanged;

    partial void OnSelectedDeviceChanged(DiscoveredDevice? value)
    {
        SelectedDeviceChanged?.Invoke(value);
    }
}
