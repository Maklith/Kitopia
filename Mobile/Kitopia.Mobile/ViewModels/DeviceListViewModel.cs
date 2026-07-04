using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kitopia.DeviceCommunication.Discovery;

namespace Kitopia.Mobile.ViewModels;

public sealed partial class DeviceListViewModel : ObservableObject
{
    public DeviceListViewModel(IDeviceDiscoveryService discoveryService)
    {
        _discoveryService = discoveryService;
    }

    private readonly IDeviceDiscoveryService _discoveryService;

    public ObservableCollection<DiscoveredDevice> Devices => _discoveryService.Devices;

    [ObservableProperty]
    private DiscoveredDevice? _selectedDevice;

    public event Action<DiscoveredDevice?>? SelectedDeviceChanged;

    partial void OnSelectedDeviceChanged(DiscoveredDevice? value)
    {
        SelectedDeviceChanged?.Invoke(value);
    }
}
