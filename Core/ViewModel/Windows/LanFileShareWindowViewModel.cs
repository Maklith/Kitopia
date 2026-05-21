using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Services.Config;
using Core.Services.DeviceCommunication;
using Core.Services.DeviceCommunication.Application;
using Core.Services.DeviceCommunication.Discovery;
using Core.Services.DeviceCommunication.Messages.Chat;
using ObservableCollections;
using PluginCore;

namespace Core.ViewModel.Windows;

public partial class LanFileShareWindowViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;
    private readonly IMessageAppService _messageAppService;
    private readonly IToastService _toastService;

    private static Dictionary<string, string> CustomNameMap
    {
        get
        {
            ConfigManger.Config.deviceCustomNames ??= new Dictionary<string, string>();
            return ConfigManger.Config.deviceCustomNames;
        }
    }

    public NotifyCollectionChangedSynchronizedViewList<DeviceModel> DiscoveredDevices => _deviceDiscoveryService.Devices;
    public ObservableCollection<ShareFileItem> SelectedFiles { get; } = new();

    [ObservableProperty]
    private bool _isSending;

    public bool HasFiles => SelectedFiles.Count > 0;
    public bool HasDevices => DiscoveredDevices.Count > 0;
    public bool CanSend => HasFiles && !IsSending;
    public string FilesHeader => HasFiles ? $"待发送文件 ({SelectedFiles.Count})" : "待发送文件";
    public string DevicesHeader => HasDevices ? $"在线设备 ({DiscoveredDevices.Count})" : "在线设备";

    public LanFileShareWindowViewModel(
        IDeviceDiscoveryService deviceDiscoveryService,
        IMessageAppService messageAppService,
        IToastService toastService)
    {
        _deviceDiscoveryService = deviceDiscoveryService;
        _messageAppService = messageAppService;
        _toastService = toastService;

        ApplySavedCustomNames();
        SelectedFiles.CollectionChanged += OnSelectedFilesCollectionChanged;
        DiscoveredDevices.CollectionChanged += OnDiscoveredDevicesCollectionChanged;
    }

    public void Dispose()
    {
        SelectedFiles.CollectionChanged -= OnSelectedFilesCollectionChanged;
        DiscoveredDevices.CollectionChanged -= OnDiscoveredDevicesCollectionChanged;
    }

    public void SetSelectedFiles(IEnumerable<string> filePaths)
    {
        SelectedFiles.Clear();

        var uniquePaths = filePaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim().Trim('"'))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var path in uniquePaths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var info = new FileInfo(path);
            SelectedFiles.Add(new ShareFileItem(info.Name, info.FullName, FormatFileSize(info.Length)));
        }
    }

    [RelayCommand]
    private void RemoveFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var item = SelectedFiles.FirstOrDefault(
            f => string.Equals(f.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (item != null)
        {
            SelectedFiles.Remove(item);
        }
    }

    [RelayCommand]
    private void ClearFiles()
    {
        SelectedFiles.Clear();
    }

    [RelayCommand]
    private async Task SendToDevice(DeviceModel? device)
    {
        if (device is null || !CanSend)
        {
            return;
        }

        var filesToSend = SelectedFiles.Select(f => f.FilePath).ToList();
        if (filesToSend.Count == 0)
        {
            _toastService.Show("局域网分享", "没有可发送的文件。", NotificationType.Warning);
            return;
        }

        IsSending = true;
        var targetName = string.IsNullOrWhiteSpace(device.DisplayName) ? device.ToString() : device.DisplayName;

        try
        {
            foreach (var filePath in filesToSend)
            {
                var fileInfo = new FileInfo(filePath);
                await using var fileStream = fileInfo.OpenRead();
                var transferId = Guid.NewGuid();
                var fileMessage = new FileChatMessage(device.Id, transferId, fileInfo.Name, fileInfo.Length);
                await SendFileToDeviceAsync(device, fileMessage, fileStream);
            }

            _toastService.Show(
                "局域网分享",
                $"已向 {targetName} 发起 {filesToSend.Count} 个文件传输请求。",
                NotificationType.Success);
        }
        catch (Exception ex)
        {
            _toastService.Show(
                "局域网分享失败",
                $"发送到 {targetName} 时出错: {ex.Message}",
                NotificationType.Error);
        }
        finally
        {
            IsSending = false;
        }
    }

    private async Task SendFileToDeviceAsync(DeviceModel device, FileChatMessage message, Stream stream)
    {
        if (string.IsNullOrWhiteSpace(device.Id))
        {
            throw new InvalidOperationException("Invalid target device identity.");
        }

        await _messageAppService.SendFileChatAsync(device.Id, message, stream);
    }

    private void OnSelectedFilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(FilesHeader));
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

        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(DevicesHeader));
    }

    partial void OnIsSendingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSend));
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

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024d:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / 1024d / 1024d:F1} MB";
        return $"{bytes / 1024d / 1024d / 1024d:F1} GB";
    }
}

public sealed class ShareFileItem
{
    public string FileName { get; }
    public string FilePath { get; }
    public string FileSizeText { get; }

    public ShareFileItem(string fileName, string filePath, string fileSizeText)
    {
        FileName = fileName;
        FilePath = filePath;
        FileSizeText = fileSizeText;
    }
}
