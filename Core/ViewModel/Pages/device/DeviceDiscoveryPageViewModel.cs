using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Services.Interfaces;
using Kitopia.DeviceCommunication.Discovery;
using ObservableCollections;

namespace Core.ViewModel.Pages.device;

public partial class DeviceDiscoveryPageViewModel : ObservableObject
{
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;
    private readonly IConfigService _config;

    public NotifyCollectionChangedSynchronizedViewList<DiscoveredDevice> DiscoveredDevices => _deviceDiscoveryService.Devices;

    public DeviceDiscoveryPageViewModel(IDeviceDiscoveryService deviceDiscoveryService, IConfigService config)
    {
        _deviceDiscoveryService = deviceDiscoveryService;
        _config = config;
    }

    [RelayCommand]
    private void SaveCustomName(DiscoveredDevice? device)
    {
        if (device is null || string.IsNullOrWhiteSpace(device.Id))
        {
            return;
        }

        var name = device.CustomName?.Trim() ?? string.Empty;
        name = string.IsNullOrEmpty(name) ? string.Empty : name;
        device.CustomName = name;
        _config.SetDeviceCustomName(device.Id, name);
    }

    [RelayCommand]
    private void ClearCustomName(DiscoveredDevice? device)
    {
        if (device is null || string.IsNullOrWhiteSpace(device.Id))
        {
            return;
        }

        device.CustomName = string.Empty;
        _config.RemoveDeviceCustomName(device.Id);
    }
}


