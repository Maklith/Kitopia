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
    private const int ClipboardPollIntervalMs = 800;

    private readonly IDeviceCommunication _deviceCommunication;
    private readonly IClipboardService _clipboardService;
    private CancellationTokenSource? _clipboardSyncCts;
    private string _lastSyncedClipboardText = string.Empty;
    private int _isApplyingRemoteClipboard;

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

    public DeviceDiscoveryPageViewModel(
        IDeviceCommunication deviceCommunication,
        IClipboardService clipboardService)
    {
        _deviceCommunication = deviceCommunication;
        _clipboardService = clipboardService;
        IsDiscovering = true;

        ApplySavedCustomNames();
        DiscoveredDevices.CollectionChanged += OnDiscoveredDevicesCollectionChanged;

        _lastSyncedClipboardText = _clipboardService.GetText() ?? string.Empty;
    }

    public void Dispose()
    {
        IsClipboardSyncEnabled = false;
        DiscoveredDevices.CollectionChanged -= OnDiscoveredDevicesCollectionChanged;
    }

    partial void OnIsClipboardSyncEnabledChanged(bool value)
    {
        if (value)
        {
            StartClipboardSync();
            return;
        }

        StopClipboardSync();
    }

    partial void OnClipboardSyncTargetDeviceChanged(DeviceModel? value)
    {
        OnPropertyChanged(nameof(ClipboardSyncTargetDisplay));
        if (!IsClipboardSyncEnabled)
        {
            return;
        }

        if (value is null)
        {
            UpdateClipboardSyncStatus("请选择要同步的设备");
            return;
        }

        UpdateClipboardSyncStatus($"已选择同步设备: {value.DisplayName}");
    }

    [RelayCommand]
    private void ToggleClipboardSyncForDevice(DeviceModel? device)
    {
        if (device is null)
        {
            return;
        }

        var currentTarget = ResolveClipboardSyncTarget();
        var isCurrentTarget = currentTarget is not null && IsSameDevice(currentTarget, device);
        if (isCurrentTarget && IsClipboardSyncEnabled)
        {
            IsClipboardSyncEnabled = false;
            return;
        }

        ClipboardSyncTargetDevice = device;
        IsClipboardSyncEnabled = true;
    }

    private void StartClipboardSync()
    {
        if (_clipboardSyncCts is not null)
        {
            return;
        }

        _lastSyncedClipboardText = _clipboardService.GetText() ?? string.Empty;
        _clipboardSyncCts = new CancellationTokenSource();
        _deviceCommunication.ClipboardTextReceived += OnClipboardTextReceived;
        if (ResolveClipboardSyncTarget() is { } target)
        {
            UpdateClipboardSyncStatus($"实时同步剪贴板已开启，目标设备: {target.DisplayName}");
        }
        else
        {
            UpdateClipboardSyncStatus("请选择要同步的设备");
        }
        _ = MonitorClipboardLoopAsync(_clipboardSyncCts.Token);
    }

    private void StopClipboardSync()
    {
        var cts = _clipboardSyncCts;
        if (cts is null)
        {
            UpdateClipboardSyncStatus("实时同步剪贴板已关闭");
            return;
        }

        _clipboardSyncCts = null;
        _deviceCommunication.ClipboardTextReceived -= OnClipboardTextReceived;
        cts.Cancel();
        cts.Dispose();
        UpdateClipboardSyncStatus("实时同步剪贴板已关闭");
    }

    private async Task MonitorClipboardLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var isApplyingRemote = Interlocked.CompareExchange(ref _isApplyingRemoteClipboard, 0, 0) == 1;
                var hasText = _clipboardService.HasText();
                if (!isApplyingRemote && hasText)
                {
                    var target = ResolveClipboardSyncTarget();
                    if (target is null)
                    {
                        UpdateClipboardSyncStatus("请选择要同步的设备");
                    }
                    else
                    {
                        var currentText = _clipboardService.GetText() ?? string.Empty;
                        var shouldBroadcast = !string.IsNullOrEmpty(currentText) &&
                                              !string.Equals(currentText, _lastSyncedClipboardText,
                                                  StringComparison.Ordinal);
                        if (shouldBroadcast)
                        {
                            _lastSyncedClipboardText = currentText;
                            await SyncClipboardTextToTargetAsync(target, currentText, token);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Clipboard sync loop error: {ex}");
            }
            try
            {
                await Task.Delay(ClipboardPollIntervalMs, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SyncClipboardTextToTargetAsync(DeviceModel target, string text, CancellationToken cancellationToken)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await _deviceCommunication.SendClipboardTextAsync(target, text);
            UpdateClipboardSyncStatus($"剪贴板已同步到 {target.DisplayName}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Clipboard sync send error: {ex}");
            UpdateClipboardSyncStatus("剪贴板同步失败");
        }
    }

    private void OnClipboardTextReceived(object? sender, DeviceClipboardReceivedEventArgs e)
    {
        if (!IsClipboardSyncEnabled || string.IsNullOrWhiteSpace(e.Text))
        {
            return;
        }

        var target = ResolveClipboardSyncTarget();
        if (target is null || !IsSameDevice(target, e.Sender))
        {
            return;
        }

        if (string.Equals(e.Text, _lastSyncedClipboardText, StringComparison.Ordinal))
        {
            return;
        }

        Interlocked.Exchange(ref _isApplyingRemoteClipboard, 1);
        try
        {
            if (!_clipboardService.SetText(e.Text))
            {
                UpdateClipboardSyncStatus("接收远端剪贴板失败");
                return;
            }

            _lastSyncedClipboardText = e.Text;
            UpdateClipboardSyncStatus($"已从 {e.Sender.DisplayName} 同步剪贴板");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Clipboard apply error: {ex}");
            UpdateClipboardSyncStatus("接收远端剪贴板失败");
        }
        finally
        {
            Interlocked.Exchange(ref _isApplyingRemoteClipboard, 0);
        }
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
        if (e.NewItems == null)
        {
            EnsureClipboardTargetStillAvailable();
            return;
        }

        foreach (var item in e.NewItems)
        {
            if (item is DeviceModel device)
            {
                ApplySavedCustomName(device);
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

        ClipboardSyncTargetDevice = null;
        if (IsClipboardSyncEnabled)
        {
            UpdateClipboardSyncStatus("同步目标设备已离线，请重新选择");
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

    private DeviceModel? ResolveClipboardSyncTarget()
    {
        var selected = ClipboardSyncTargetDevice;
        if (selected is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(selected.Id))
        {
            var matchedById = DiscoveredDevices.FirstOrDefault(device =>
                string.Equals(device.Id, selected.Id, StringComparison.Ordinal));
            if (matchedById is not null)
            {
                return matchedById;
            }
        }

        if (selected.Port <= 0)
        {
            return null;
        }

        return DiscoveredDevices.FirstOrDefault(device =>
            string.Equals(device.Address.ToString(), selected.Address.ToString(), StringComparison.OrdinalIgnoreCase) &&
            device.Port == selected.Port);
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
