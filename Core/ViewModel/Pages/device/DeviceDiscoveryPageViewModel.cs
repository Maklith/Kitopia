using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Services.Config;
using Core.Services.Interfaces;
using Core.Utils;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Ursa.Controls;

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
        StartDiscovery();

        ApplySavedCustomNames();
        DiscoveredDevices.CollectionChanged += OnDiscoveredDevicesCollectionChanged;

        _deviceCommunication.MessageReceived += OnMessageReceived;
        _deviceCommunication.FileTransferRequested += OnFileTransferRequested;
        _deviceCommunication.StreamReceived += OnStreamReceived;
        _deviceCommunication.TransferInterrupted += OnTransferInterrupted;
    }

    public void Dispose()
    {
        DiscoveredDevices.CollectionChanged -= OnDiscoveredDevicesCollectionChanged;
        _deviceCommunication.MessageReceived -= OnMessageReceived;
        _deviceCommunication.FileTransferRequested -= OnFileTransferRequested;
        _deviceCommunication.StreamReceived -= OnStreamReceived;
        _deviceCommunication.TransferInterrupted -= OnTransferInterrupted;
        StopDiscovery();
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

    private void OnMessageReceived(object? sender, DeviceMessageReceivedEventArgs e)
    {
        var senderName = GetDeviceDisplayName(e.Sender);
        ServiceManager.Services.GetService<IToastService>()!.Show(
            $"\u6d88\u606f\u6765\u81ea {senderName}",
            e.Message);
    }

    private void OnTransferInterrupted(object? sender, TransferInterruptionEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            ServiceManager.Services.GetService<IToastService>()!.Show(
                "\u4f20\u8f93\u4e2d\u65ad",
                $"\u8bf7\u6c42ID: {e.RequestId}\n\u539f\u56e0: {e.Reason}\n\u65b9\u5411: {(e.IsSending ? "\u53d1\u9001" : "\u63a5\u6536")}");
        });
    }

    private async void OnFileTransferRequested(object? sender, FileTransferRequestEventArgs e)
    {
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var senderName = GetDeviceDisplayName(e.Sender);
            var fileSize = FormatFileSize(e.FileSize);
            var accepted = false;
            try
            {
                await ServiceManager.Services.GetService<IContentDialog>()!.ShowDialogAsync(null,
                    new DialogContent
                    {
                        Title = "\u6587\u4ef6\u63a5\u6536\u8bf7\u6c42",
                        Content = $"\u63a5\u6536\u5230\u6587\u4ef6 '{e.FileName}' ({fileSize})\uff0c\u53d1\u9001\u65b9\uff1a{senderName}",
                        PrimaryButtonText = "\u63a5\u6536",
                        SecondaryButtonText = "\u53d6\u6d88",
                        PrimaryAction = () =>
                        {
                            accepted = true;
                        }
                    });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Dialog Error: {ex}");
                accepted = true;
            }

            string? savePath = null;
            if (accepted)
            {
                try
                {
                    var lifetime = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                    if (lifetime?.MainWindow != null)
                    {
                        var file = await lifetime.MainWindow.StorageProvider.SaveFilePickerAsync(
                            new Avalonia.Platform.Storage.FilePickerSaveOptions
                            {
                                Title = "\u4fdd\u5b58\u6587\u4ef6",
                                SuggestedFileName = e.FileName
                            });

                        if (file != null)
                        {
                            savePath = file.Path.LocalPath;
                        }
                        else
                        {
                            accepted = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Save Picker Error: {ex}");
                }
            }

            await _deviceCommunication.RespondToFileRequestAsync(e.Sender, e.RequestId, accepted, savePath);
        });
    }

    private async void OnStreamReceived(object? sender, DeviceStreamReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.SavedPath))
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await (ServiceManager.Services.GetService<IContentDialog>()?.ShowDialogAsync(null, new DialogContent
                {
                    Title = "\u6587\u4ef6\u63a5\u6536\u6210\u529f",
                    Content = $"\u6765\u81ea {GetDeviceDisplayName(e.Sender)} \u7684\u6587\u4ef6\u5df2\u4fdd\u5b58\u81f3: {e.SavedPath}",
                    PrimaryButtonText = "\u786e\u5b9a"
                }) ?? Task.CompletedTask);
            });
            return;
        }

        if (string.IsNullOrEmpty(e.MetaData))
        {
            return;
        }

        try
        {
            var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var meta = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(e.MetaData, options);

            string? type = null;
            if (meta.TryGetProperty("Type", out var typeProp))
            {
                type = typeProp.GetString();
            }

            if (string.IsNullOrEmpty(type) && meta.TryGetProperty("type", out typeProp))
            {
                type = typeProp.GetString();
            }

            if (string.Equals(type, "FileTransfer", StringComparison.OrdinalIgnoreCase))
            {
                string? rawFileName = null;
                if (meta.TryGetProperty("FileName", out var nameProp))
                {
                    rawFileName = nameProp.GetString();
                }

                if (string.IsNullOrEmpty(rawFileName) && meta.TryGetProperty("fileName", out nameProp))
                {
                    rawFileName = nameProp.GetString();
                }

                if (!string.IsNullOrEmpty(rawFileName))
                {
                    var fileName = Path.GetFileName(rawFileName);

                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        try
                        {
                            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                            if (lifetime?.MainWindow != null)
                            {
                                var file = await lifetime.MainWindow.StorageProvider.SaveFilePickerAsync(
                                    new Avalonia.Platform.Storage.FilePickerSaveOptions
                                    {
                                        Title = "\u4fdd\u5b58\u63a5\u6536\u5230\u7684\u6587\u4ef6",
                                        SuggestedFileName = fileName
                                    });

                                if (file != null)
                                {
                                    var path = file.Path.LocalPath;
                                    using var fs = new FileStream(path, FileMode.Create);
                                    if (e.Stream.CanSeek)
                                    {
                                        e.Stream.Position = 0;
                                    }

                                    await e.Stream.CopyToAsync(fs);

                                    await (ServiceManager.Services.GetService<IContentDialog>()?.ShowDialogAsync(null,
                                        new DialogContent
                                        {
                                            Title = "\u6587\u4ef6\u63a5\u6536\u6210\u529f",
                                            Content = $"\u6765\u81ea {GetDeviceDisplayName(e.Sender)} \u7684\u6587\u4ef6\u5df2\u4fdd\u5b58\u81f3: {path}",
                                            PrimaryButtonText = "\u786e\u5b9a"
                                        }) ?? Task.CompletedTask);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Save Manual Stream Error: {ex}");
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Stream Receive Error: {ex}");
        }
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

        var targetName = GetDeviceDisplayName(device);
        try
        {
            await _deviceCommunication.SendMessageAsync(device, MessageToSend);
            ServiceManager.Services.GetService<IToastService>()!.Show(
                "\u6d88\u606f\u5df2\u53d1\u9001",
                $"\u5df2\u53d1\u9001\u5230 {targetName}",
                NotificationType.Success);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Message Send Error: {ex}");
            ServiceManager.Services.GetService<IToastService>()!.Show(
                "\u6d88\u606f\u53d1\u9001\u5931\u8d25",
                $"\u53d1\u9001\u5230 {targetName} \u65f6\u51fa\u9519: {ex.Message}",
                NotificationType.Error);
        }
    }

    [RelayCommand]
    public async Task SendFile(DeviceModel device)
    {
        var lifetime = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (lifetime?.MainWindow == null)
        {
            return;
        }

        var files = await lifetime.MainWindow.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select File to Send",
            AllowMultiple = false
        });

        if (files == null || files.Count == 0)
        {
            return;
        }

        var file = files[0];
        var path = file.Path.LocalPath;
        var fileName = Path.GetFileName(path);
        var fileSize = new FileInfo(path).Length;
        var targetName = GetDeviceDisplayName(device);
        try
        {
            await _deviceCommunication.RequestFileTransferAsync(device, path);
            ServiceManager.Services.GetService<IToastService>()!.Show(
                "\u6587\u4ef6\u53d1\u9001\u5b8c\u6210",
                $"\u5df2\u53d1\u9001 {fileName} ({FormatFileSize(fileSize)}) \u5230 {targetName}",
                NotificationType.Success);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"File Send Error: {ex}");
            ServiceManager.Services.GetService<IToastService>()!.Show(
                "\u6587\u4ef6\u53d1\u9001\u5931\u8d25",
                $"\u53d1\u9001\u5230 {targetName} \u65f6\u51fa\u9519: {ex.Message}",
                NotificationType.Error);
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

    private static string GetDeviceDisplayName(DeviceModel? device)
    {
        if (device is null)
        {
            return "\u672a\u77e5\u8bbe\u5907";
        }

        if (!string.IsNullOrWhiteSpace(device.CustomName))
        {
            if (!string.IsNullOrWhiteSpace(device.Name))
            {
                return $"{device.CustomName} ({device.Name})";
            }

            return device.CustomName;
        }

        if (!string.IsNullOrWhiteSpace(device.Name))
        {
            return device.Name;
        }

        return "\u672a\u77e5\u8bbe\u5907";
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        var unitIndex = 0;
        double value = bytes;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        if (unitIndex == 0)
        {
            return $"{bytes:N0} B";
        }

        return $"{value:0.##} {units[unitIndex]} ({bytes:N0} B)";
    }
}
