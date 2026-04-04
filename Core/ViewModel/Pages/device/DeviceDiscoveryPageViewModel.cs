using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Services.Config;
using PluginCore;

namespace Core.ViewModel.Pages.device;

public partial class DeviceDiscoveryPageViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceCommunication _deviceCommunication;
    private int _isRequestingClipboardSync;

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
    private bool _isClipboardSyncEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClipboardSyncTargetDisplay))]
    private DeviceModel? _clipboardSyncTargetDevice;

    [ObservableProperty]
    private string _clipboardSyncStatus = "实时同步剪贴板已关闭";

    public string ClipboardSyncTargetDisplay => ClipboardSyncTargetDevice?.DisplayName ?? "未选择";

    public DeviceDiscoveryPageViewModel(IDeviceCommunication deviceCommunication)
    {
        _deviceCommunication = deviceCommunication;
        IsDiscovering = true;

        ApplySavedCustomNames();
        DiscoveredDevices.CollectionChanged += OnDiscoveredDevicesCollectionChanged;
        _deviceCommunication.ClipboardSyncStateChanged += OnClipboardSyncStateChanged;

        IsClipboardSyncEnabled = _deviceCommunication.IsClipboardSyncEnabled;
        var currentTarget = _deviceCommunication.ClipboardSyncTargetDevice;
        ClipboardSyncTargetDevice = currentTarget is null
            ? null
            : ResolveDiscoveredDevice(currentTarget) ?? currentTarget;
        ClipboardSyncStatus = IsClipboardSyncEnabled && ClipboardSyncTargetDevice is not null
            ? $"已与 {ClipboardSyncTargetDevice.DisplayName} 建立双向剪贴板同步"
            : "实时同步剪贴板已关闭";
    }

    public void Dispose()
    {
        DiscoveredDevices.CollectionChanged -= OnDiscoveredDevicesCollectionChanged;
        _deviceCommunication.ClipboardSyncStateChanged -= OnClipboardSyncStateChanged;
    }

    [RelayCommand]
    private async Task ToggleClipboardSyncForDevice(DeviceModel? device)
    {
        if (device is null)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _isRequestingClipboardSync, 1, 0) != 0)
        {
            return;
        }

        try
        {
            var currentTarget = ResolveClipboardSyncTarget();
            var isCurrentTarget = currentTarget is not null && IsSameDevice(currentTarget, device);
            if (isCurrentTarget && IsClipboardSyncEnabled)
            {
                _deviceCommunication.DisableClipboardSync();
                return;
            }

            var resolvedTarget = ResolveDiscoveredDevice(device) ?? device;
            await _deviceCommunication.EnableClipboardSyncAsync(resolvedTarget);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Enable clipboard sync failed: {ex}");
            UpdateClipboardSyncStatus("同步请求失败，请重试");
        }
        finally
        {
            Interlocked.Exchange(ref _isRequestingClipboardSync, 0);
        }
    }

    private void OnClipboardSyncStateChanged(object? sender, DeviceClipboardSyncStateChangedEventArgs e)
    {
        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsClipboardSyncEnabled = e.IsEnabled;
            ClipboardSyncTargetDevice = e.TargetDevice is null
                ? null
                : ResolveDiscoveredDevice(e.TargetDevice) ?? e.TargetDevice;
            UpdateClipboardSyncStatus(e.Status);
        });
    }

    private void UpdateClipboardSyncStatus(string text)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ClipboardSyncStatus = text;
            return;
        }

        _ = Dispatcher.UIThread.InvokeAsync(() => ClipboardSyncStatus = text);
    }

    private void OnDiscoveredDevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is DeviceModel device)
                {
                    ApplySavedCustomName(device);
                }
            }
        }

        EnsureClipboardTargetStillAvailable();
    }

    private void EnsureClipboardTargetStillAvailable()
    {
        if (ClipboardSyncTargetDevice is null)
        {
            return;
        }

        if (ResolveClipboardSyncTarget() is not null)
        {
            return;
        }

        if (IsClipboardSyncEnabled)
        {
            _deviceCommunication.DisableClipboardSync();
            return;
        }

        ClipboardSyncTargetDevice = null;
        UpdateClipboardSyncStatus("同步目标设备已离线，请重新选择");
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

    private DeviceModel? ResolveDiscoveredDevice(DeviceModel candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.Id))
        {
            var matchedById = DiscoveredDevices.FirstOrDefault(device =>
                string.Equals(device.Id, candidate.Id, StringComparison.Ordinal));
            if (matchedById is not null)
            {
                return matchedById;
            }
        }

        if (candidate.Port <= 0)
        {
            return null;
        }

        return DiscoveredDevices.FirstOrDefault(device =>
            string.Equals(device.Address.ToString(), candidate.Address.ToString(), StringComparison.OrdinalIgnoreCase) &&
            device.Port == candidate.Port);
    }

    private DeviceModel? ResolveClipboardSyncTarget()
    {
        var selected = ClipboardSyncTargetDevice;
        if (selected is null)
        {
            return null;
        }

        return ResolveDiscoveredDevice(selected);
    }

    private static bool IsSameDevice(DeviceModel a, DeviceModel b)
    {
        if (!string.IsNullOrWhiteSpace(a.Id) && !string.IsNullOrWhiteSpace(b.Id))
        {
            return string.Equals(a.Id, b.Id, StringComparison.Ordinal);
        }

        return a.Port > 0 &&
               b.Port > 0 &&
               a.Port == b.Port &&
               string.Equals(a.Address.ToString(), b.Address.ToString(), StringComparison.OrdinalIgnoreCase);
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
