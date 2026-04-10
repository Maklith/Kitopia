using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Net.Quic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Services;
using Core.Services.Config;
using Core.Services.DeviceCommunication.Discovery;
using PluginCore;
using Serilog;
using Serilog.Core;

namespace Core.ViewModel.Pages.device;

public partial class DeviceDiscoveryPageViewModel : ObservableObject, IDisposable {
    private static readonly ILogger Logger = LogManager.Logger.ForContext<DeviceDiscoveryPageViewModel>();
    private readonly IDeviceDiscoveryService  _deviceDiscoveryService;
    public ObservableCollection<DeviceModel> DiscoveredDevices => _deviceDiscoveryService.Devices;

    [ObservableProperty] private bool _isDiscovering;

    [ObservableProperty] private bool _isClipboardSyncEnabled;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ClipboardSyncTargetDisplay))]
    private DeviceModel? _clipboardSyncTargetDevice;

    [ObservableProperty] private string _clipboardSyncStatus = "实时同步剪贴板已关闭";

    public string ClipboardSyncTargetDisplay => ClipboardSyncTargetDevice?.DisplayName ?? "未选择";

    public DeviceDiscoveryPageViewModel(IDeviceDiscoveryService deviceDiscoveryService) {
        _deviceDiscoveryService = deviceDiscoveryService;
        IsDiscovering = true;
        DiscoveredDevices.CollectionChanged += OnDiscoveredDevicesCollectionChanged;
    }

    public void Dispose() {
        DiscoveredDevices.CollectionChanged -= OnDiscoveredDevicesCollectionChanged;
       
    }

    [RelayCommand]
    private async Task ToggleClipboardSyncForDevice(DeviceModel? device) {
        if (device is null) {
            return;
        }
        
    }

    
    private void OnDiscoveredDevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        
    }
    
    
    [RelayCommand]
    public void StartDiscovery() {
        // try {
        //     _deviceDiscoveryService.Start(new DiscoveryAnnouncement {
        //         DeviceId =ConfigManger.Config.devicePersistentId,
        //         DeviceName = ConfigManger.Config.deviceBroadcastName,
        //         Port = _transportService.AdvertisedPort,
        //         SupportsQuic = QuicConnection.IsSupported&& QuicListener.IsSupported
        //     });
        //     IsDiscovering = true;
        // }
        // catch {
        //     IsDiscovering = false;
        // }
    }

    [RelayCommand]
    public void StopDiscovery() {
        _deviceDiscoveryService.Stop();
        IsDiscovering = false;
    }

    [RelayCommand]
    private void SaveCustomName(DeviceModel? device) {
        if (device is null || string.IsNullOrWhiteSpace(device.Id)) {
            return;
        }

        var name = device.CustomName?.Trim() ?? string.Empty;
        device.CustomName = string.IsNullOrEmpty(name) ? string.Empty : name;
        ConfigManger.Config.deviceCustomNames[device.Id] = device.CustomName;
        ConfigManger.Save("KitopiaConfig");
    }

    [RelayCommand]
    private void ClearCustomName(DeviceModel? device) {
        if (device is null || string.IsNullOrWhiteSpace(device.Id)) {
            return;
        }

        device.CustomName = string.Empty;
        ConfigManger.Config.deviceCustomNames.Remove(device.Id);
        ConfigManger.Save("KitopiaConfig");
    }
}