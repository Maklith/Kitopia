using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Services;
using Core.Services.Config;
using Core.Services.DeviceCommunication.Discovery;
using ObservableCollections;
using PluginCore;
using Serilog;

namespace Core.ViewModel.Pages.device;

public partial class DeviceDiscoveryPageViewModel : ObservableObject
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<DeviceDiscoveryPageViewModel>();
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;

    public NotifyCollectionChangedSynchronizedViewList<DeviceModel> DiscoveredDevices => _deviceDiscoveryService.Devices;

    public DeviceDiscoveryPageViewModel(IDeviceDiscoveryService deviceDiscoveryService)
    {
        _deviceDiscoveryService = deviceDiscoveryService;
    }

    [RelayCommand]
    private void SaveCustomName(DeviceModel? device)
    {
        if (device is null || string.IsNullOrWhiteSpace(device.Id))
        {
            return;
        }

        var name = device.CustomName?.Trim() ?? string.Empty;
        device.CustomName = string.IsNullOrEmpty(name) ? string.Empty : name;
        ConfigManger.Config.deviceCustomNames[device.Id] = device.CustomName;
        ConfigManger.Save("KitopiaConfig");
    }

    [RelayCommand]
    private void ClearCustomName(DeviceModel? device)
    {
        if (device is null || string.IsNullOrWhiteSpace(device.Id))
        {
            return;
        }

        device.CustomName = string.Empty;
        ConfigManger.Config.deviceCustomNames.Remove(device.Id);
        ConfigManger.Save("KitopiaConfig");
    }
}


