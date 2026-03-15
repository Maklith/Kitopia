using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Services.Config;
using PluginCore;

namespace Core.ViewModel.Pages.device;

public partial class DeviceDiscoveryPageViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceCommunication _deviceCommunication;
    private static Dictionary<string, string> CustomNameMap
    {
        get
        {
            ConfigManger.Config.deviceCustomNames ??= new Dictionary<string, string>();
            return ConfigManger.Config.deviceCustomNames;
        }
    }

    public ObservableCollection<DeviceModel> DiscoveredDevices => _deviceCommunication.DiscoveredDevices;

    [ObservableProperty]
    private bool _isDiscovering;

    [ObservableProperty]
    private string _messageToSend = "Hello from Kitopia!";

    public DeviceDiscoveryPageViewModel(IDeviceCommunication deviceCommunication)
    {
        _deviceCommunication = deviceCommunication;
        IsDiscovering = true;

        ApplySavedCustomNames();
        DiscoveredDevices.CollectionChanged += OnDiscoveredDevicesCollectionChanged;
    }

    public void Dispose()
    {
        DiscoveredDevices.CollectionChanged -= OnDiscoveredDevicesCollectionChanged;
    }

    private void OnDiscoveredDevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems == null)
        {
            return;
        }

        foreach (var item in e.NewItems)
        {
            if (item is DeviceModel device)
            {
                ApplySavedCustomName(device);
            }
        }
    }

    private void ApplySavedCustomNames()
    {
        foreach (var device in DiscoveredDevices)
        {
            ApplySavedCustomName(device);
        }
    }

    private static void ApplySavedCustomName(DeviceModel device)
    {
        if (string.IsNullOrWhiteSpace(device.Id))
        {
            return;
        }

        if (CustomNameMap.TryGetValue(device.Id, out var customName) &&
            !string.IsNullOrWhiteSpace(customName))
        {
            device.CustomName = customName.Trim();
            return;
        }

        device.CustomName = string.Empty;
    }

    [RelayCommand]
    public void StartDiscovery()
    {
        try
        {
            _deviceCommunication.StartDiscovery();
            IsDiscovering = true;
        }
        catch
        {
            IsDiscovering = false;
        }
    }

    [RelayCommand]
    public void StopDiscovery()
    {
        _deviceCommunication.StopDiscovery();
        IsDiscovering = false;
    }

    [RelayCommand]
    public async Task SendMessage(DeviceModel device)
    {
        if (string.IsNullOrWhiteSpace(MessageToSend))
        {
            return;
        }

        try
        {
            await _deviceCommunication.SendMessageAsync(device, MessageToSend);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Message Send Error: {ex}");
        }
    }

    [RelayCommand]
    public async Task SendFile(DeviceModel device)
    {
        try
        {
            await _deviceCommunication.RequestFileTransferAsync(device);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"File Send Error: {ex}");
        }
    }

    [RelayCommand]
    private void SaveCustomName(DeviceModel? device)
    {
        if (device is null || string.IsNullOrWhiteSpace(device.Id))
        {
            return;
        }

        var name = device.CustomName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name))
        {
            CustomNameMap.Remove(device.Id);
            device.CustomName = string.Empty;
        }
        else
        {
            CustomNameMap[device.Id] = name;
            device.CustomName = name;
        }

        ConfigManger.Save("KitopiaConfig");
    }

    [RelayCommand]
    private void ClearCustomName(DeviceModel? device)
    {
        if (device is null || string.IsNullOrWhiteSpace(device.Id))
        {
            return;
        }

        if (CustomNameMap.Remove(device.Id))
        {
            ConfigManger.Save("KitopiaConfig");
        }

        device.CustomName = string.Empty;
    }

}
