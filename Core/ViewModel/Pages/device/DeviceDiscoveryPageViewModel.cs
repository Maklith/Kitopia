using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Services;
using Core.Services.Config;
using Core.Services.DeviceCommunication;
using Core.Services.DeviceCommunication.Application;
using Core.Services.DeviceCommunication.Discovery;
using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Routing;
using Core.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using ObservableCollections;
using PluginCore;
using Serilog;
using Serilog.Core;

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


