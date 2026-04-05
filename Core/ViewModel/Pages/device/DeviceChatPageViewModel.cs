﻿using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Net;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Services.DeviceCommunication;
using PluginCore;

namespace Core.ViewModel.Pages.device;

public partial class DeviceChatPageViewModel : ObservableObject, IDisposable
{
    private const int MessagePageSize = 50;
    private readonly IDeviceCommunication _deviceCommunication;
    private readonly IDeviceChatHistoryStore _chatHistoryStore;
    private readonly IToastService _toastService;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _loadedMessagesPeerKey;
    private long? _oldestLoadedMessageId;
    private bool _isChatInterfaceActive;

    public ObservableCollection<DeviceChatConversationItem> Conversations { get; } = [];
    public ObservableCollection<DeviceChatMessageItem> Messages { get; } = [];

    [ObservableProperty]
    private DeviceChatConversationItem? _selectedConversation;

    [ObservableProperty]
    private string _messageToSend = string.Empty;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private bool _isLoadingHistory;

    [ObservableProperty]
    private bool _hasMoreHistory;

    public bool HasSelectedConversation => SelectedConversation is not null;
    public bool CanSendMessage => SelectedConversation is not null && !string.IsNullOrWhiteSpace(MessageToSend);
    public bool CanSendFile => SelectedConversation is not null;
    public bool CanLoadMoreHistory => HasSelectedConversation && HasMoreHistory && !IsLoadingHistory;

    public string SelectedConversationTitle => SelectedConversation?.DisplayName ?? "请选择会话";

    public string SelectedConversationSubtitle => SelectedConversation?.Subtitle ?? "请在左侧选择设备会话。";

    public DeviceChatPageViewModel(
        IDeviceCommunication deviceCommunication,
        IDeviceChatHistoryStore chatHistoryStore,
        IToastService toastService)
    {
        _deviceCommunication = deviceCommunication;
        _chatHistoryStore = chatHistoryStore;
        _toastService = toastService;

        _deviceCommunication.DiscoveredDevices.CollectionChanged += OnDiscoveredDevicesCollectionChanged;
        _deviceCommunication.MessageReceived += OnDeviceMessageReceivedForActivePage;
        _chatHistoryStore.MessageStored += OnMessageStored;

        _ = RefreshDataAsync();
    }

    public void Dispose()
    {
        SetChatInterfaceActive(false);
        _deviceCommunication.DiscoveredDevices.CollectionChanged -= OnDiscoveredDevicesCollectionChanged;
        _deviceCommunication.MessageReceived -= OnDeviceMessageReceivedForActivePage;
        _chatHistoryStore.MessageStored -= OnMessageStored;
    }

    public void SetChatInterfaceActive(bool isActive)
    {
        if (_isChatInterfaceActive == isActive)
        {
            return;
        }

        _isChatInterfaceActive = isActive;
        if (_isChatInterfaceActive)
        {
            _deviceCommunication.FileTransferRequested += OnFileTransferRequestedForActivePage;
        }
        else
        {
            _deviceCommunication.FileTransferRequested -= OnFileTransferRequestedForActivePage;
        }
    }

    partial void OnSelectedConversationChanged(DeviceChatConversationItem? value)
    {
        OnPropertyChanged(nameof(HasSelectedConversation));
        OnPropertyChanged(nameof(CanSendMessage));
        OnPropertyChanged(nameof(CanSendFile));
        OnPropertyChanged(nameof(CanLoadMoreHistory));
        OnPropertyChanged(nameof(SelectedConversationTitle));
        OnPropertyChanged(nameof(SelectedConversationSubtitle));
        LoadMoreMessagesCommand.NotifyCanExecuteChanged();

        var peerKey = value?.PeerKey;
        if (string.Equals(_loadedMessagesPeerKey, peerKey, StringComparison.Ordinal))
        {
            return;
        }

        _oldestLoadedMessageId = null;
        HasMoreHistory = false;
        _loadedMessagesPeerKey = peerKey;
        _ = LoadMessagesForConversationAsync(peerKey);
    }

    partial void OnMessageToSendChanged(string value)
    {
        OnPropertyChanged(nameof(CanSendMessage));
    }

    partial void OnIsLoadingHistoryChanged(bool value)
    {
        OnPropertyChanged(nameof(CanLoadMoreHistory));
        LoadMoreMessagesCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasMoreHistoryChanged(bool value)
    {
        OnPropertyChanged(nameof(CanLoadMoreHistory));
        LoadMoreMessagesCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await RefreshDataAsync(SelectedConversation?.PeerKey);
    }

    [RelayCommand(CanExecute = nameof(CanLoadMoreHistory))]
    private async Task LoadMoreMessagesAsync()
    {
        var peerKey = SelectedConversation?.PeerKey;
        var beforeMessageId = _oldestLoadedMessageId;
        if (string.IsNullOrWhiteSpace(peerKey) || beforeMessageId is null || IsLoadingHistory)
        {
            return;
        }

        IsLoadingHistory = true;
        try
        {
            var history = await _chatHistoryStore.GetMessagesAsync(
                peerKey,
                limit: MessagePageSize,
                beforeMessageId: beforeMessageId.Value);
            var items = history.Select(BuildMessageItem).ToList();
            var hasMore = history.Count >= MessagePageSize;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!string.Equals(SelectedConversation?.PeerKey, peerKey, StringComparison.Ordinal))
                {
                    return;
                }

                for (var i = items.Count - 1; i >= 0; i--)
                {
                    var item = items[i];
                    if (Messages.Any(existing => existing.Id == item.Id))
                    {
                        continue;
                    }

                    Messages.Insert(0, item);
                }

                _oldestLoadedMessageId = Messages.Count > 0 ? Messages[0].Id : null;
                HasMoreHistory = hasMore && _oldestLoadedMessageId is not null;
                ReevaluatePendingFileRequestStates();
            });
        }
        finally
        {
            IsLoadingHistory = false;
        }
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        var conversation = SelectedConversation;
        var text = MessageToSend?.Trim();
        if (conversation is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var target = ResolveTargetDevice(conversation);
        if (target is null)
        {
            _toastService.Show(
                "设备聊天",
                "目标设备当前不可达。",
                NotificationType.Warning);
            return;
        }

        try
        {
            MessageToSend = string.Empty;
            await _deviceCommunication.SendMessageAsync(target, text);
        }
        catch (Exception ex)
        {
            _toastService.Show(
                "发送失败",
                ex.Message,
                NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task SendFileAsync()
    {
        var conversation = SelectedConversation;
        if (conversation is null)
        {
            return;
        }

        var target = ResolveTargetDevice(conversation);
        if (target is null)
        {
            _toastService.Show(
                "设备聊天",
                "目标设备当前不可达。",
                NotificationType.Warning);
            return;
        }

        try
        {
            await _deviceCommunication.RequestFileTransferAsync(target);
        }
        catch (Exception ex)
        {
            _toastService.Show(
                "文件发送失败",
                ex.Message,
                NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task AcceptFileRequestAsync(DeviceChatMessageItem? message)
    {
        if (message is null || !message.IsIncomingFileRequestPending || string.IsNullOrWhiteSpace(message.RequestId))
        {
            return;
        }

        var conversation = SelectedConversation;
        if (conversation is null)
        {
            _toastService.Show("设备聊天", "未找到对应会话。", NotificationType.Warning);
            return;
        }

        var target = ResolveTargetDevice(conversation);
        if (target is null)
        {
            _toastService.Show("设备聊天", "目标设备当前不可达。", NotificationType.Warning);
            return;
        }

        var suggestedFileName = string.IsNullOrWhiteSpace(message.FileName) ? "接收文件" : message.FileName;
        var savePath = await PickSaveFilePathAsync("保存接收文件", suggestedFileName);
        if (string.IsNullOrWhiteSpace(savePath))
        {
            return;
        }

        try
        {
            await _deviceCommunication.RespondToFileRequestAsync(target, message.RequestId, true, savePath);
            ClearPendingFileRequestState(message.RequestId);
            _toastService.Show("文件接收", "已同意文件请求，等待对方开始传输。", NotificationType.Information);
        }
        catch (Exception ex)
        {
            _toastService.Show("文件接收失败", ex.Message, NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task RejectFileRequestAsync(DeviceChatMessageItem? message)
    {
        if (message is null || !message.IsIncomingFileRequestPending || string.IsNullOrWhiteSpace(message.RequestId))
        {
            return;
        }

        var conversation = SelectedConversation;
        if (conversation is null)
        {
            _toastService.Show("设备聊天", "未找到对应会话。", NotificationType.Warning);
            return;
        }

        var target = ResolveTargetDevice(conversation);
        if (target is null)
        {
            _toastService.Show("设备聊天", "目标设备当前不可达。", NotificationType.Warning);
            return;
        }

        try
        {
            await _deviceCommunication.RespondToFileRequestAsync(target, message.RequestId, false);
            ClearPendingFileRequestState(message.RequestId);
            _toastService.Show("文件接收", "已拒绝文件请求。", NotificationType.Information);
        }
        catch (Exception ex)
        {
            _toastService.Show("文件接收失败", ex.Message, NotificationType.Error);
        }
    }

    [RelayCommand]
    private void OpenFile(DeviceChatMessageItem? message)
    {
        if (message is null || !message.CanOpenFile)
        {
            return;
        }

        if (!File.Exists(message.FilePath))
        {
            _toastService.Show(
                "打开文件",
                "文件路径不可用。",
                NotificationType.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = message.FilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _toastService.Show(
                "打开文件失败",
                ex.Message,
                NotificationType.Error);
        }
    }

    private void OnDiscoveredDevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _ = RefreshDataAsync(SelectedConversation?.PeerKey);
    }

    private void OnMessageStored(object? sender, DeviceChatMessage message)
    {
        _ = HandleMessageStoredAsync(message);
    }

    private async Task HandleMessageStoredAsync(DeviceChatMessage message)
    {
        var selectedPeerKey = SelectedConversation?.PeerKey;
        var isCurrentConversation =
            !string.IsNullOrWhiteSpace(selectedPeerKey) &&
            string.Equals(selectedPeerKey, message.PeerKey, StringComparison.Ordinal);

        if (isCurrentConversation)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!Messages.Any(item => item.Id == message.Id))
                {
                    Messages.Add(BuildMessageItem(message));
                    if (_oldestLoadedMessageId is null)
                    {
                        _oldestLoadedMessageId = message.Id;
                    }
                }

                ReevaluatePendingFileRequestStates();
            });
        }

        await RefreshDataAsync(selectedPeerKey);
    }

    private void OnDeviceMessageReceivedForActivePage(object? sender, DeviceMessageReceivedEventArgs e)
    {
        var selected = SelectedConversation;
        if (selected is not null && IsSameConversationPeer(selected, e.Sender))
        {
            return;
        }

        var senderName = string.IsNullOrWhiteSpace(e.Sender.DisplayName) ? "未知设备" : e.Sender.DisplayName;
        var preview = e.Message?.Trim() ?? string.Empty;
        if (preview.Length > 80)
        {
            preview = preview[..80] + "...";
        }

        if (string.IsNullOrWhiteSpace(preview))
        {
            preview = "（空消息）";
        }

        Dispatcher.UIThread.Post(() =>
        {
            _toastService.Show(
                "收到新消息",
                $"{senderName}: {preview}",
                NotificationType.Information);
        });
    }

    private void OnFileTransferRequestedForActivePage(object? sender, FileTransferRequestEventArgs e)
    {
        // 仅用于标记聊天界面激活，收到文件请求时由聊天消息列表中的操作按钮处理。
    }

    private async Task RefreshDataAsync(string? preserveSelectedPeerKey = null, string? forceSelectPeerKey = null)
    {
        await _refreshLock.WaitAsync();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsRefreshing = true);

            var summaries = await _chatHistoryStore.GetConversationsAsync(limit: 300);
            var merged = new Dictionary<string, DeviceChatConversationItem>(StringComparer.Ordinal);

            foreach (var summary in summaries)
            {
                merged[summary.PeerKey] = BuildConversationFromSummary(summary);
            }

            foreach (var device in _deviceCommunication.DiscoveredDevices)
            {
                var peerKey = DeviceChatPeerKey.Build(device);
                if (merged.TryGetValue(peerKey, out var existing))
                {
                    merged[peerKey] = existing with
                    {
                        PeerId = string.IsNullOrWhiteSpace(device.Id) ? existing.PeerId : device.Id,
                        DisplayName = device.DisplayName,
                        PeerAddress = device.Address.ToString(),
                        PeerPort = device.Port,
                        IsOnline = true
                    };
                    continue;
                }

                merged[peerKey] = new DeviceChatConversationItem
                {
                    PeerKey = peerKey,
                    PeerId = device.Id,
                    DisplayName = device.DisplayName,
                    PeerAddress = device.Address.ToString(),
                    PeerPort = device.Port,
                    LastPreview = "在线设备",
                    LastTimestampUtc = DateTime.MinValue,
                    IsOnline = true
                };
            }

            var ordered = merged.Values
                .OrderByDescending(item => item.LastTimestampUtc)
                .ThenByDescending(item => item.IsOnline)
                .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var selectedPeerKey = forceSelectPeerKey;
            if (string.IsNullOrWhiteSpace(selectedPeerKey))
            {
                selectedPeerKey = preserveSelectedPeerKey;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Conversations.Clear();
                foreach (var item in ordered)
                {
                    Conversations.Add(item);
                }

                DeviceChatConversationItem? selected = null;
                if (!string.IsNullOrWhiteSpace(selectedPeerKey))
                {
                    selected = Conversations.FirstOrDefault(item => item.PeerKey == selectedPeerKey);
                }

                selected ??= Conversations.FirstOrDefault();
                SelectedConversation = selected;
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsRefreshing = false);
            _refreshLock.Release();
        }
    }

    private async Task LoadMessagesForConversationAsync(string? peerKey)
    {
        if (string.IsNullOrWhiteSpace(peerKey))
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Messages.Clear();
                _oldestLoadedMessageId = null;
                HasMoreHistory = false;
                IsLoadingHistory = false;
            });
            return;
        }

        IsLoadingHistory = true;
        try
        {
            var history = await _chatHistoryStore.GetMessagesAsync(peerKey, limit: MessagePageSize);
            var items = history.Select(BuildMessageItem).ToList();
            var oldestMessageId = history.Count > 0 ? history[0].Id : (long?)null;
            var hasMore = history.Count >= MessagePageSize;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!string.Equals(SelectedConversation?.PeerKey, peerKey, StringComparison.Ordinal))
                {
                    return;
                }

                Messages.Clear();
                foreach (var item in items)
                {
                    Messages.Add(item);
                }

                _oldestLoadedMessageId = oldestMessageId;
                HasMoreHistory = hasMore && _oldestLoadedMessageId is not null;
                ReevaluatePendingFileRequestStates();
            });
        }
        finally
        {
            IsLoadingHistory = false;
        }
    }

    private DeviceModel? ResolveTargetDevice(DeviceChatConversationItem conversation)
    {
        if (!string.IsNullOrWhiteSpace(conversation.PeerId))
        {
            var knownById = _deviceCommunication.DiscoveredDevices
                .FirstOrDefault(device => string.Equals(device.Id, conversation.PeerId, StringComparison.Ordinal));
            if (knownById is not null)
            {
                return knownById;
            }
        }

        var knownByEndpoint = _deviceCommunication.DiscoveredDevices.FirstOrDefault(device =>
            string.Equals(device.Address.ToString(), conversation.PeerAddress, StringComparison.OrdinalIgnoreCase) &&
            device.Port == conversation.PeerPort);
        if (knownByEndpoint is not null)
        {
            return knownByEndpoint;
        }

        if (conversation.PeerPort <= 0 || !IPAddress.TryParse(conversation.PeerAddress, out var address))
        {
            return null;
        }

        return new DeviceModel
        {
            Id = conversation.PeerId,
            Name = conversation.DisplayName,
            Address = address,
            Port = conversation.PeerPort,
            LastSeen = DateTime.UtcNow
        };
    }

    private static bool IsSameConversationPeer(DeviceChatConversationItem conversation, DeviceModel sender)
    {
        if (!string.IsNullOrWhiteSpace(conversation.PeerId) && !string.IsNullOrWhiteSpace(sender.Id))
        {
            return string.Equals(conversation.PeerId, sender.Id, StringComparison.Ordinal);
        }

        if (conversation.PeerPort <= 0 || sender.Port <= 0)
        {
            return false;
        }

        return conversation.PeerPort == sender.Port &&
               string.Equals(conversation.PeerAddress, sender.Address.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string?> PickSaveFilePathAsync(string title, string suggestedFileName)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (lifetime?.MainWindow == null)
            {
                return null;
            }

            var file = await lifetime.MainWindow.StorageProvider.SaveFilePickerAsync(
                new Avalonia.Platform.Storage.FilePickerSaveOptions
                {
                    Title = title,
                    SuggestedFileName = suggestedFileName
                });

            return file?.Path.LocalPath;
        });
    }

    private void ClearPendingFileRequestState(string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        for (var i = 0; i < Messages.Count; i++)
        {
            var item = Messages[i];
            if (!item.IsIncomingFileRequestPending)
            {
                continue;
            }

            if (!string.Equals(item.RequestId, requestId, StringComparison.Ordinal))
            {
                continue;
            }

            Messages[i] = item with { IsIncomingFileRequestPending = false };
        }
    }

    private void ReevaluatePendingFileRequestStates()
    {
        var resolvedRequestIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var message in Messages)
        {
            if (string.IsNullOrWhiteSpace(message.RequestId))
            {
                continue;
            }

            if (IsResolvedFileRequestStatus(message.Status))
            {
                resolvedRequestIds.Add(message.RequestId);
            }
        }

        for (var i = 0; i < Messages.Count; i++)
        {
            var message = Messages[i];
            var shouldPending =
                message.IsIncoming &&
                message.EntryType == DeviceChatEntryType.FileRequest &&
                string.Equals(message.Status, "requested", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(message.RequestId) &&
                !resolvedRequestIds.Contains(message.RequestId);

            if (message.IsIncomingFileRequestPending == shouldPending)
            {
                continue;
            }

            Messages[i] = message with { IsIncomingFileRequestPending = shouldPending };
        }
    }

    private static bool IsResolvedFileRequestStatus(string status)
    {
        return status switch
        {
            "accepted" => true,
            "rejected" => true,
            "failed" => true,
            "timeout" => true,
            "completed" => true,
            _ => false
        };
    }

    private static DeviceChatConversationItem BuildConversationFromSummary(DeviceChatConversation summary)
    {
        return new DeviceChatConversationItem
        {
            PeerKey = summary.PeerKey,
            PeerId = summary.PeerId,
            DisplayName = DeviceChatPeerKey.ResolveDisplayName(summary.PeerName, summary.PeerAddress),
            PeerAddress = summary.PeerAddress,
            PeerPort = summary.PeerPort,
            LastPreview = BuildConversationPreview(summary),
            LastTimestampUtc = summary.LastTimestampUtc,
            IsOnline = false
        };
    }

    private static string BuildConversationPreview(DeviceChatConversation summary)
    {
        return summary.LastEntryType switch
        {
            DeviceChatEntryType.File => $"文件: {summary.LastFileName}",
            DeviceChatEntryType.FileRequest => $"文件请求: {summary.LastFileName}",
            DeviceChatEntryType.TransferStatus => string.IsNullOrWhiteSpace(summary.LastContent)
                ? summary.LastStatus
                : summary.LastContent,
            _ => summary.LastContent
        };
    }

    private static DeviceChatMessageItem BuildMessageItem(DeviceChatMessage message)
    {
        var text = BuildMessageText(message);
        var isFileMessage = message.EntryType == DeviceChatEntryType.File || !string.IsNullOrWhiteSpace(message.FileName);
        var localTime = message.TimestampUtc.Kind == DateTimeKind.Utc
            ? message.TimestampUtc.ToLocalTime()
            : message.TimestampUtc;

        var sender = message.Direction switch
        {
            DeviceChatDirection.Outgoing => "我",
            DeviceChatDirection.Incoming => string.IsNullOrWhiteSpace(message.PeerName)
                ? "对方"
                : message.PeerName,
            _ => "系统"
        };

        var fileDisplay = string.Empty;
        if (isFileMessage)
        {
            var sizePart = message.FileSize > 0 ? $" ({FormatFileSize(message.FileSize)})" : string.Empty;
            fileDisplay = string.IsNullOrWhiteSpace(message.FileName)
                ? $"文件{sizePart}"
                : $"{message.FileName}{sizePart}";
        }

        var footer = localTime.ToString("yyyy-MM-dd HH:mm:ss");
        if (!string.IsNullOrWhiteSpace(message.Status))
        {
            footer = $"{footer} [{LocalizeStatus(message.Status)}]";
        }

        return new DeviceChatMessageItem
        {
            Id = message.Id,
            SenderLabel = sender,
            Text = text,
            HasText = !string.IsNullOrWhiteSpace(text),
            IsFile = isFileMessage,
            FileDisplay = fileDisplay,
            FilePath = message.FilePath,
            CanOpenFile = !string.IsNullOrWhiteSpace(message.FilePath),
            Footer = footer,
            IsOutgoing = message.Direction == DeviceChatDirection.Outgoing,
            IsIncoming = message.Direction == DeviceChatDirection.Incoming,
            IsSystem = message.Direction == DeviceChatDirection.System,
            EntryType = message.EntryType,
            FileName = message.FileName,
            RequestId = message.RequestId,
            Status = message.Status,
            IsIncomingFileRequestPending =
                message.Direction == DeviceChatDirection.Incoming &&
                message.EntryType == DeviceChatEntryType.FileRequest &&
                string.Equals(message.Status, "requested", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static string BuildMessageText(DeviceChatMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            return message.Content;
        }

        return message.EntryType switch
        {
            DeviceChatEntryType.Text => string.Empty,
            DeviceChatEntryType.File => "文件消息",
            DeviceChatEntryType.FileRequest => "文件传输请求",
            DeviceChatEntryType.TransferStatus => "传输状态更新",
            _ => string.Empty
        };
    }

    private static string LocalizeStatus(string status)
    {
        return status switch
        {
            "requested" => "已请求",
            "accepted" => "已接受",
            "rejected" => "已拒绝",
            "completed" => "已完成",
            "failed" => "失败",
            "timeout" => "超时",
            _ => status
        };
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var unitIndex = 0;
        double value = bytes;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0 ? $"{bytes} B" : $"{value:0.##} {units[unitIndex]}";
    }
}

public sealed record DeviceChatConversationItem
{
    public string PeerKey { get; init; } = string.Empty;
    public string PeerId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string PeerAddress { get; init; } = string.Empty;
    public int PeerPort { get; init; }
    public string LastPreview { get; init; } = string.Empty;
    public DateTime LastTimestampUtc { get; init; } = DateTime.MinValue;
    public bool IsOnline { get; init; }

    public string LastTimestampText =>
        LastTimestampUtc == DateTime.MinValue
            ? string.Empty
            : LastTimestampUtc.ToLocalTime().ToString("MM-dd HH:mm");

    public string Subtitle
    {
        get
        {
            var endpoint = string.IsNullOrWhiteSpace(PeerAddress)
                ? "未知地址"
                : PeerPort > 0
                    ? $"{PeerAddress}:{PeerPort}"
                    : PeerAddress;

            return IsOnline ? $"{endpoint} · 在线" : $"{endpoint} · 离线";
        }
    }
}

public sealed record DeviceChatMessageItem
{
    public long Id { get; init; }
    public string SenderLabel { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public bool HasText { get; init; }
    public bool IsFile { get; init; }
    public string FileDisplay { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public bool CanOpenFile { get; init; }
    public string Footer { get; init; } = string.Empty;
    public bool IsOutgoing { get; init; }
    public bool IsIncoming { get; init; }
    public bool IsSystem { get; init; }
    public DeviceChatEntryType EntryType { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string RequestId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public bool IsIncomingFileRequestPending { get; init; }
}
