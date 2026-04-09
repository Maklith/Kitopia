using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Core.Services.Config;
using Core.Services.DeviceCommunication.Clipboard;
using Core.Services.DeviceCommunication.Discovery;
using Core.Services.DeviceCommunication.FileTransfer;
using Core.Services.DeviceCommunication.Presentation;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Requests;
using Core.Services.DeviceCommunication.Transport;
using PluginCore;

namespace Core.Services.DeviceCommunication;

public class DeviceCommunicationService : IDeviceCommunication, IDevicePacketActions, IDisposable
{
    private readonly Guid _myId = LoadOrCreateDeviceIdFromConfig();

    private readonly IRequestTracker _requestTracker;
    private readonly IDeviceDiscoveryService _discoveryService;
    private readonly ITransportService _transportService;
    private readonly IClipboardSyncService _clipboardSyncService;
    private readonly IFilePickerService _filePickerService;
    private readonly IClipboardAssetExtractor _clipboardAssetExtractor;
    private readonly IFileTransferService _fileTransferService;
    private readonly IChatHistoryService _chatHistoryService;
    private readonly INotificationService _notificationService;
    private readonly IDeviceEventBus _eventBus;
    private readonly IPacketRouter _packetRouter;
    private CancellationTokenSource? _lifecycleCts;

    private readonly ConcurrentDictionary<string, string> _pendingDownloads = new();
    private readonly ConcurrentDictionary<string, IncomingFileRequestContext> _pendingIncomingFileRequests = new();
    private readonly ConcurrentDictionary<string, IToastProgressHandle> _sendingTransferToasts = new();
    private readonly ConcurrentDictionary<string, IToastProgressHandle> _receivingTransferToasts = new();
    private static readonly TimeSpan TransferToastUpdateInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan ClipboardSyncRequestTimeout = TimeSpan.FromSeconds(30);
    private readonly object _chatWindowStateLock = new();
    private readonly object _lifecycleLock = new();
    private int _isChatWindowActive;
    private DeviceModel? _activeChatWindowDevice;

    public ObservableCollection<DeviceModel> DiscoveredDevices { get; } = new();
    public bool IsClipboardSyncEnabled => _clipboardSyncService.IsEnabled;
    public DeviceModel? ClipboardSyncTargetDevice => _clipboardSyncService.TargetDevice;

    public event EventHandler<DeviceStreamReceivedEventArgs>? StreamReceived;
    public event EventHandler<DeviceCommunicationEventArgs>? CommunicationEvent
    {
        add => _eventBus.CommunicationEvent += value;
        remove => _eventBus.CommunicationEvent -= value;
    }

    public DeviceCommunicationService(
        IClipboardService clipboardService,
        IRequestTracker requestTracker,
        IDeviceDiscoveryService discoveryService,
        ITransportService transportService,
        IFilePickerService filePickerService,
        IClipboardAssetExtractor clipboardAssetExtractor,
        IChatHistoryService chatHistoryService,
        INotificationService notificationService,
        IDeviceEventBus eventBus)
    {
        _requestTracker = requestTracker;
        _discoveryService = discoveryService;
        _transportService = transportService;
        _filePickerService = filePickerService;
        _clipboardAssetExtractor = clipboardAssetExtractor;
        _chatHistoryService = chatHistoryService;
        _notificationService = notificationService;
        _eventBus = eventBus;
        _clipboardSyncService = new ClipboardSyncService(
            clipboardService,
            requestTracker,
            transportService,
            discoveryService,
            GetAdvertisedPort,
            () => _myId,
            GetLocalDisplayName);
        _fileTransferService = new FileTransferService(
            requestTracker,
            transportService,
            filePickerService,
            clipboardAssetExtractor,
            chatHistoryService,
            notificationService,
            GetAdvertisedPort,
            () => _myId,
            GetLocalDisplayName,
            ShouldShowExternalToastForSender);
        _packetRouter = new PacketRouter(
            new IPacketHandler[]
            {
                new MessagePacketHandler(this),
                new ClipboardPacketHandler(this),
                new ClipboardSyncRequestHandler(this),
                new ClipboardSyncResponseHandler(this),
                new FileRequestHandler(this),
                new FileResponseHandler(this),
                new FileTransferHandler(this),
                new LegacyPacketHandler(this)
            },
            HandleUnknownPacketAsync);
        _discoveryService.DeviceDiscovered += OnDeviceDiscovered;
        _discoveryService.DeviceUpdated += OnDeviceUpdated;
        _discoveryService.DeviceLost += OnDeviceLost;
        _transportService.PacketReceived += OnTransportPacketReceived;
        _clipboardSyncService.StateChanged += OnClipboardSyncStateChanged;
        _clipboardSyncService.Authorized += OnClipboardSyncAuthorized;
        _fileTransferService.FileRequestReceived += OnFileTransferRequestReceived;
        _fileTransferService.TransferProgress += OnFileTransferProgress;
        _fileTransferService.TransferCompleted += OnFileTransferCompleted;
        _fileTransferService.TransferInterrupted += OnFileTransferInterrupted;
        _fileTransferService.StreamReceived += OnFileTransferStreamReceived;
    }

    private static Guid LoadOrCreateDeviceIdFromConfig()
    {
        try
        {
            var config = ConfigManger.Config;
            if (config != null &&
                Guid.TryParse(config.devicePersistentId, out var existingId) &&
                existingId != Guid.Empty)
            {
                return existingId;
            }

            var newId = Guid.NewGuid();
            if (config != null)
            {
                config.devicePersistentId = newId.ToString("D");
                ConfigManger.Save("KitopiaConfig");
            }
            return newId;
        }
        catch
        {
            return Guid.NewGuid();
        }
    }

    private static string GetLocalDisplayName()
    {
        try
        {
            var configuredName = ConfigManger.Config.deviceBroadcastName;
            if (!string.IsNullOrWhiteSpace(configuredName))
            {
                return configuredName.Trim();
            }
        }
        catch
        {
            // Config may not be initialized yet during early startup.
        }

        return Environment.MachineName;
    }

    private async Task SaveChatRecordAsync(
        DeviceModel? peer,
        DeviceChatDirection direction,
        DeviceChatEntryType entryType,
        string content = "",
        string fileName = "",
        string filePath = "",
        long fileSize = 0,
        string requestId = "",
        string status = "")
    {
        try
        {
            var peerId = peer?.Id ?? string.Empty;
            var peerAddress = peer?.Address?.ToString() ?? string.Empty;
            var peerPort = peer?.Port ?? 0;

            await _chatHistoryService.AppendAsync(new DeviceChatMessage
            {
                PeerKey = DeviceChatPeerKey.Build(peerId, peerAddress, peerPort),
                PeerId = peerId,
                PeerName = GetDeviceDisplayName(peer),
                PeerAddress = peerAddress,
                PeerPort = peerPort,
                Direction = direction,
                EntryType = entryType,
                Content = content ?? string.Empty,
                FileName = fileName ?? string.Empty,
                FilePath = filePath ?? string.Empty,
                FileSize = fileSize,
                RequestId = requestId ?? string.Empty,
                Status = status ?? string.Empty,
                TimestampUtc = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatHistory] Save failed: {ex}");
        }
    }

    private void NotifyTransferInterrupted(string requestId, string reason, bool isSending)
    {
        FailTransferToast(requestId, reason, isSending);
        _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                _notificationService.Show(
                    "\u4f20\u8f93\u4e2d\u65ad",
                    $"\u8bf7\u6c42ID: {requestId}\n\u539f\u56e0: {reason}\n\u65b9\u5411: {(isSending ? "\u53d1\u9001" : "\u63a5\u6536")}",
                    NotificationType.Error);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Transfer] Interrupt toast error: {ex}");
            }

            if (!string.IsNullOrEmpty(requestId))
            {
                var args = new TransferInterruptionEventArgs(requestId, reason, isSending);
                PublishCommunicationEvent(DeviceCommunicationEventType.TransferInterrupted, args);
            }
        });
    }

    private void PublishMessageReceived(DeviceModel sender, string message)
    {
        var args = new DeviceMessageReceivedEventArgs(sender, message);
        PublishCommunicationEvent(DeviceCommunicationEventType.MessageReceived, args);
    }

    private void PublishClipboardReceived(DeviceModel sender, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var args = new DeviceClipboardReceivedEventArgs(sender, text);
        PublishCommunicationEvent(DeviceCommunicationEventType.ClipboardTextReceived, args);
    }

    private void PublishClipboardSyncAuthorized(DeviceModel peer, bool initiatedByPeer)
    {
        var args = new DeviceClipboardSyncAuthorizedEventArgs(peer, initiatedByPeer);
        PublishCommunicationEvent(DeviceCommunicationEventType.ClipboardSyncAuthorized, args);
    }

    private void PublishClipboardSyncStateChanged(bool isEnabled, DeviceModel? target, string status)
    {
        var clonedTarget = target is null ? null : CloneDeviceModel(target);
        var args = new DeviceClipboardSyncStateChangedEventArgs(isEnabled, clonedTarget, status);
        PublishCommunicationEvent(DeviceCommunicationEventType.ClipboardSyncStateChanged, args);
    }

    private void OnClipboardSyncStateChanged(object? sender, DeviceClipboardSyncStateChangedEventArgs e)
    {
        PublishClipboardSyncStateChanged(e.IsEnabled, e.TargetDevice, e.Status);
    }

    private void OnClipboardSyncAuthorized(object? sender, DeviceClipboardSyncAuthorizedEventArgs e)
    {
        PublishClipboardSyncAuthorized(e.Peer, e.InitiatedByPeer);
    }

    private void OnFileTransferRequestReceived(object? sender, FileTransferRequestEventArgs e)
    {
        ExecuteByChatWindowState(
            e.Sender,
            whenChatWindowMatchesSender: () =>
            {
                PublishCommunicationEvent(DeviceCommunicationEventType.FileTransferRequested, e);
            },
            whenChatWindowMismatchedOrInactive: () =>
            {
                ShowIncomingFileRequestActionToast(e.RequestId, e.Sender, e.FileName, e.FileSize);
            });
    }

    private void OnFileTransferProgress(object? sender, FileTransferProgressEventArgs e)
    {
        PublishCommunicationEvent(DeviceCommunicationEventType.FileTransferProgress, e);
    }

    private void OnFileTransferCompleted(object? sender, FileTransferCompletedEventArgs e)
    {
        PublishCommunicationEvent(DeviceCommunicationEventType.FileTransferCompleted, e);
    }

    private void OnFileTransferInterrupted(object? sender, TransferInterruptionEventArgs e)
    {
        PublishCommunicationEvent(DeviceCommunicationEventType.TransferInterrupted, e);
    }

    private void OnFileTransferStreamReceived(object? sender, DeviceStreamReceivedEventArgs e)
    {
        StreamReceived?.Invoke(this, e);
    }

    private void PublishFileTransferProgress(
        DeviceModel peer,
        string requestId,
        string fileName,
        long transferredBytes,
        long totalBytes,
        bool isSending)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        var args = new FileTransferProgressEventArgs(
            requestId,
            fileName,
            transferredBytes,
            totalBytes,
            isSending,
            CloneDeviceModel(peer));
        PublishCommunicationEvent(DeviceCommunicationEventType.FileTransferProgress, args);
    }

    private void PublishFileTransferCompleted(
        DeviceModel peer,
        string requestId,
        string fileName,
        long fileSize,
        string filePath,
        bool isSending)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        var args = new FileTransferCompletedEventArgs(
            requestId,
            fileName,
            fileSize,
            filePath,
            isSending,
            CloneDeviceModel(peer));
        PublishCommunicationEvent(DeviceCommunicationEventType.FileTransferCompleted, args);
    }

    private void PublishCommunicationEvent(DeviceCommunicationEventType type, EventArgs payload)
    {
        _eventBus.Publish(type, payload);
    }

    private void OnDeviceDiscovered(object? sender, DeviceDiscoveryEventArgs e)
    {
        _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            var existing = ResolveDiscoveredDevice(e.Device);
            if (existing is not null)
            {
                UpdateDiscoveredDevice(existing, e.Device);
                return;
            }

            DiscoveredDevices.Add(CloneDeviceModel(e.Device));
        });
    }

    private void OnDeviceUpdated(object? sender, DeviceDiscoveryEventArgs e)
    {
        _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            var existing = ResolveDiscoveredDevice(e.Device);
            if (existing is null)
            {
                DiscoveredDevices.Add(CloneDeviceModel(e.Device));
                return;
            }

            UpdateDiscoveredDevice(existing, e.Device);
        });
    }

    private void OnDeviceLost(object? sender, DeviceDiscoveryEventArgs e)
    {
        _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            var existing = ResolveDiscoveredDevice(e.Device);
            if (existing is null)
            {
                return;
            }

            DiscoveredDevices.Remove(existing);
        });
    }

    private static void UpdateDiscoveredDevice(DeviceModel target, DeviceModel source)
    {
        target.LastSeen = source.LastSeen;
        target.Name = source.Name;
        if (ShouldReplaceDiscoveredAddress(target.Address, source.Address))
        {
            target.Address = source.Address;
        }

        target.Port = source.Port;
    }

    private void OnTransportPacketReceived(object? sender, TransportPacketReceivedEventArgs e)
    {
        try
        {
            DispatchPacketAsync(e.Packet, e.Payload, e.Sender).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Transport] Dispatch error: {ex}");
        }
    }

    private void ExecuteByChatWindowState(
        DeviceModel sender,
        Action whenChatWindowMatchesSender,
        Action whenChatWindowMismatchedOrInactive)
    {
        if (IsChatWindowMatchingSender(sender))
        {
            whenChatWindowMatchesSender();
            return;
        }

        whenChatWindowMismatchedOrInactive();
    }

    private bool IsChatWindowMatchingSender(DeviceModel sender)
    {
        lock (_chatWindowStateLock)
        {
            return _isChatWindowActive == 1 &&
                   _activeChatWindowDevice is not null &&
                   IsSameDevice(_activeChatWindowDevice, sender);
        }
    }

    private bool ShouldShowExternalToastForSender(DeviceModel sender)
    {
        return !IsChatWindowMatchingSender(sender);
    }

    private async Task<bool> PromptIncomingClipboardSyncRequestAsync(DeviceModel sender)
    {
        return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                var senderName = GetDeviceDisplayName(sender);
                var decisionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _notificationService.Show(new ToastRequest
                {
                    Header = "剪贴板同步请求",
                    Text = $"{senderName} 请求建立双向剪贴板同步",
                    NotificationType = NotificationType.Information,
                    AutoCloseDelay = ClipboardSyncRequestTimeout,
                    ShowCloseButton = false,
                    Actions =
                    [
                        new ToastAction
                        {
                            Text = "同意",
                            IsPrimary = true,
                            Callback = () => decisionSource.TrySetResult(true)
                        },
                        new ToastAction
                        {
                            Text = "拒绝",
                            Callback = () => decisionSource.TrySetResult(false)
                        }
                    ]
                });

                var completedTask = await Task.WhenAny(
                    decisionSource.Task,
                    Task.Delay(ClipboardSyncRequestTimeout));
                return completedTask == decisionSource.Task && decisionSource.Task.Result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClipboardSync] Consent prompt error: {ex}");
                return false;
            }
        });
    }

    private async Task HandleIncomingFileRequestAsync(PacketMetadata packet, DeviceModel sender)
    {
        if (string.IsNullOrWhiteSpace(packet.RequestId))
        {
            return;
        }

        try
        {
            _pendingIncomingFileRequests[packet.RequestId] = new IncomingFileRequestContext
            {
                FileName = packet.FileName,
                FileSize = packet.Size,
                Sender = CloneDeviceModel(sender)
            };

            await SaveChatRecordAsync(
                sender,
                DeviceChatDirection.Incoming,
                DeviceChatEntryType.FileRequest,
                content: "收到文件传输请求。",
                fileName: packet.FileName,
                fileSize: packet.Size,
                requestId: packet.RequestId,
                status: "requested");

            var requestEventArgs = new FileTransferRequestEventArgs(
                packet.RequestId,
                packet.FileName,
                packet.Size,
                CloneDeviceModel(sender));

            ExecuteByChatWindowState(
                sender,
                whenChatWindowMatchesSender: () =>
                {
                    PublishCommunicationEvent(DeviceCommunicationEventType.FileTransferRequested, requestEventArgs);
                },
                whenChatWindowMismatchedOrInactive: () =>
                {
                    ShowIncomingFileRequestActionToast(packet.RequestId, sender, packet.FileName, packet.Size);
                });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FileReq] Handle request error: {ex}");
            _pendingIncomingFileRequests.TryRemove(packet.RequestId, out _);
            await SaveChatRecordAsync(
                sender,
                DeviceChatDirection.System,
                DeviceChatEntryType.TransferStatus,
                content: $"处理文件请求失败：{ex.Message}",
                fileName: packet.FileName,
                fileSize: packet.Size,
                requestId: packet.RequestId,
                status: "failed");
            try
            {
                await SendBooleanResponseAsync(sender, PacketTypes.FileResponse, packet.RequestId, false);
            }
            catch
            {
            }
        }
    }

    private async Task<string?> PickFileToSendAsync()
    {
        return await _filePickerService.PickFileToSendAsync();
    }

    private void ShowIncomingFileRequestActionToast(string requestId, DeviceModel sender, string fileName, long fileSize)
    {
        var senderName = GetDeviceDisplayName(sender);
        var displayFileName = string.IsNullOrWhiteSpace(fileName) ? "未命名文件" : fileName;
        var sizeText = fileSize > 0 ? $" ({FormatFileSize(fileSize)})" : string.Empty;
        _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                _notificationService.Show(new ToastRequest
                {
                    Header = "收到文件请求",
                    Text = $"{senderName} 请求发送 {displayFileName}{sizeText}",
                    NotificationType = NotificationType.Information,
                    AutoCloseDelay = TimeSpan.FromSeconds(30),
                    ShowCloseButton = false,
                    Actions =
                    [
                        new ToastAction
                        {
                            Text = "同意",
                            IsPrimary = true,
                            Callback = () => _ = HandleIncomingFileRequestDecisionFromToastAsync(requestId, displayFileName, true)
                        },
                        new ToastAction
                        {
                            Text = "拒绝",
                            Callback = () => _ = HandleIncomingFileRequestDecisionFromToastAsync(requestId, displayFileName, false)
                        }
                    ]
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FileReq] Action toast error: {ex}");
            }
        });
    }

    private async Task HandleIncomingFileRequestDecisionFromToastAsync(string requestId, string fileName, bool accepted)
    {
        string? savePath = null;
        if (accepted)
        {
            var suggestedName = string.IsNullOrWhiteSpace(fileName) ? "接收文件" : fileName;
            savePath = await _filePickerService.PickSaveFilePathAsync("保存文件", suggestedName);
            if (string.IsNullOrWhiteSpace(savePath))
            {
                return;
            }
        }

        try
        {
            await _fileTransferService.RespondToFileRequestAsync(requestId, accepted, savePath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FileReq] Toast decision error: {ex}");
            _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    _notificationService.Show("处理文件请求失败", ex.Message, NotificationType.Error);
                }
                catch
                {
                }
            });
        }
    }

    private async Task<string?> PickSaveFilePathAsync(string title, string suggestedFileName)
    {
        return await _filePickerService.PickSaveFilePathAsync(title, suggestedFileName);
    }

    private void ShowIncomingFileSavedToast(DeviceModel sender, string savedPath)
    {
        var senderName = GetDeviceDisplayName(sender);
        _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                _notificationService.Show(
                    "文件接收成功",
                    $"来自 {senderName} 的文件已保存至: {savedPath}",
                    NotificationType.Success);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FileTransfer] Success toast error: {ex}");
            }
        });
    }

    private static DeviceModel CloneDeviceModel(DeviceModel source)
    {
        return new DeviceModel
        {
            Id = source.Id,
            Name = source.Name,
            CustomName = source.CustomName,
            Address = source.Address,
            Port = source.Port,
            LastSeen = source.LastSeen
        };
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

    private PacketMetadata CreatePacketMetadata(
        string type,
        string content = "",
        string requestId = "",
        string fileName = "",
        long size = 0,
        bool accepted = false)
    {
        return new PacketMetadata
        {
            Type = type,
            Content = content,
            RequestId = requestId,
            FileName = fileName,
            Size = size,
            Accepted = accepted,
            SenderPort = GetAdvertisedPort(),
            SenderId = _myId.ToString(),
            SenderName = GetLocalDisplayName()
        };
    }

    private Task SendPacketMetadataAsync(DeviceModel target, PacketMetadata metadata)
    {
        return SendStreamAsync(target, Stream.Null, JsonSerializer.Serialize(metadata));
    }

    private async Task<RequestDecision> SendBooleanRequestAsync(
        DeviceModel target,
        PacketMetadata requestMeta,
        TimeSpan timeout,
        string duplicateRequestError,
        Func<Task>? onRequestSent = null)
    {
        if (string.IsNullOrWhiteSpace(requestMeta.RequestId))
        {
            throw new InvalidOperationException("请求标识不能为空。");
        }

        return await _requestTracker.WaitForBooleanResponseAsync(
            requestMeta.RequestId,
            async () =>
            {
                await SendPacketMetadataAsync(target, requestMeta);
                if (onRequestSent is not null)
                {
                    await onRequestSent();
                }
            },
            timeout,
            duplicateRequestError);
    }

    private void ResolvePendingBooleanRequest(string requestId, bool accepted)
    {
        _requestTracker.Resolve(requestId, accepted);
    }

    private static bool TryDeserializePacketMetadata(string? metaData, out PacketMetadata packet)
    {
        if (!string.IsNullOrWhiteSpace(metaData))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<PacketMetadata>(
                    metaData,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (parsed is not null)
                {
                    packet = parsed;
                    if (string.IsNullOrWhiteSpace(packet.Type))
                    {
                        packet.Type = PacketTypes.Legacy;
                    }
                    return true;
                }
            }
            catch
            {
            }
        }

        packet = new PacketMetadata
        {
            Type = PacketTypes.Legacy,
            Meta = metaData ?? string.Empty
        };
        return true;
    }

    private Task SendBooleanResponseAsync(DeviceModel target, string responseType, string requestId, bool accepted)
    {
        var responseMeta = CreatePacketMetadata(
            type: responseType,
            requestId: requestId,
            accepted: accepted);
        return SendPacketMetadataAsync(target, responseMeta);
    }

    private Action<long>? CreateTransferProgressHandler(
        string requestId,
        bool isSending,
        string fileName,
        long totalBytes,
        string remoteName,
        DeviceModel? peer = null)
    {
        if (totalBytes <= 0)
        {
            return null;
        }

        long transferredBytes = 0;
        int lastPercent = -1;
        var lastUpdate = DateTime.MinValue;
        var progressLock = new object();

        return bytes =>
        {
            var copied = Interlocked.Add(ref transferredBytes, bytes);
            var percent = (int)Math.Min(100, copied * 100d / totalBytes);
            var now = DateTime.UtcNow;

            lock (progressLock)
            {
                if (percent == lastPercent && now - lastUpdate < TransferToastUpdateInterval)
                {
                    return;
                }

                lastPercent = percent;
                lastUpdate = now;
            }

            UpdateTransferToastProgress(
                requestId,
                isSending,
                fileName,
                copied,
                totalBytes,
                remoteName);

            if (peer is not null)
            {
                PublishFileTransferProgress(peer, requestId, fileName, copied, totalBytes, isSending);
            }
        };
    }

    private int GetAdvertisedPort()
    {
        return _transportService.AdvertisedPort;
    }

    public void SetChatWindowActive(bool isActive, DeviceModel? device)
    {
        lock (_chatWindowStateLock)
        {
            _isChatWindowActive = isActive ? 1 : 0;
            _activeChatWindowDevice = isActive && device is not null
                ? CloneDeviceModel(device)
                : null;
        }
    }

    public void StartDiscovery()
    {
        lock (_lifecycleLock)
        {
            StopDiscoveryCore();
            _lifecycleCts = new CancellationTokenSource();
            _transportService.StartAsync(_lifecycleCts.Token).GetAwaiter().GetResult();
            _discoveryService.Start(new DiscoveryAnnouncement
            {
                DeviceId = _myId.ToString(),
                DeviceName = GetLocalDisplayName(),
                Port = _transportService.AdvertisedPort,
                SupportsQuic = _transportService.SupportsQuic
            });
        }
    }

    public void StopDiscovery()
    {
        lock (_lifecycleLock)
        {
            StopDiscoveryCore();
        }
    }

    private void StopDiscoveryCore()
    {
        var cts = _lifecycleCts;
        _lifecycleCts = null;
        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch
            {
            }
            cts.Dispose();
        }

        _discoveryService.Stop();
        _transportService.StopAsync().GetAwaiter().GetResult();
        _clipboardSyncService.Disable();
        _requestTracker.CancelAll();
        _fileTransferService.CancelAll();
    }

    public async Task SendMessageAsync(DeviceModel target, string message)
    {
        var targetName = GetDeviceDisplayName(target);
        try
        {
            var meta = CreatePacketMetadata(type: PacketTypes.Message, content: message);
            await SendPacketMetadataAsync(target, meta);
            await SaveChatRecordAsync(
                target,
                DeviceChatDirection.Outgoing,
                DeviceChatEntryType.Text,
                content: message,
                status: "sent");
            _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                _notificationService.Show(
                    "\u6d88\u606f\u5df2\u53d1\u9001",
                    $"\u5df2\u53d1\u9001\u5230 {targetName}",
                    NotificationType.Success);
            });
        }
        catch (Exception ex)
        {
            await SaveChatRecordAsync(
                target,
                DeviceChatDirection.Outgoing,
                DeviceChatEntryType.Text,
                content: message,
                status: $"failed:{ex.Message}");
            _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                _notificationService.Show(
                    "\u6d88\u606f\u53d1\u9001\u5931\u8d25",
                    $"\u53d1\u9001\u5230 {targetName} \u65f6\u51fa\u9519: {ex.Message}",
                    NotificationType.Error);
            });
            throw;
        }
    }

    public async Task SendClipboardTextAsync(DeviceModel target, string text)
    {
        await _clipboardSyncService.SendTextAsync(target, text);
    }

    public async Task<bool> RequestClipboardSyncAsync(DeviceModel target)
    {
        return await _clipboardSyncService.RequestAsync(
            target,
            ClipboardSyncRequestTimeout,
            "无法跟踪剪贴板同步请求。");
    }

    public async Task<bool> EnableClipboardSyncAsync(DeviceModel target)
    {
        return await _clipboardSyncService.EnableAsync(
            target,
            ClipboardSyncRequestTimeout,
            "无法跟踪剪贴板同步请求。");
    }

    public void DisableClipboardSync()
    {
        _clipboardSyncService.Disable();
    }

    public async Task RequestFileTransferAsync(DeviceModel target, string? filePath = null)
    {
        await _fileTransferService.RequestFileTransferAsync(target, filePath);
    }

    public async Task RequestImageTransferAsync(DeviceModel target, string? imagePath = null)
    {
        await _fileTransferService.RequestImageTransferAsync(target, imagePath);
    }

    public async Task<int> RequestClipboardTransferAsync(DeviceModel target)
    {
        return await _fileTransferService.RequestClipboardTransferAsync(target);
    }

    private async Task RequestFileTransferInternalAsync(DeviceModel target, string filePath, string transferKindLabel)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException(filePath);

        var fileInfo = new FileInfo(filePath);
        var requestId = Guid.NewGuid().ToString();
        var requestMeta = CreatePacketMetadata(
            type: PacketTypes.FileRequest,
            requestId: requestId,
            fileName: fileInfo.Name,
            size: fileInfo.Length);

        Task SaveFileRecordAsync(
            DeviceChatDirection direction,
            DeviceChatEntryType entryType,
            string content,
            string status)
        {
            return SaveChatRecordAsync(
                target,
                direction,
                entryType,
                content: content,
                fileName: fileInfo.Name,
                filePath: fileInfo.FullName,
                fileSize: fileInfo.Length,
                requestId: requestId,
                status: status);
        }

        try
        {
            var decision = await SendBooleanRequestAsync(
                target,
                requestMeta,
                TimeSpan.FromMinutes(1),
                "无法跟踪文件传输请求。",
                onRequestSent: () => SaveFileRecordAsync(
                    DeviceChatDirection.Outgoing,
                    DeviceChatEntryType.FileRequest,
                    $"已发起{transferKindLabel}传输请求。",
                    "requested"));

            switch (decision)
            {
                case RequestDecision.Accepted:
                    await SaveFileRecordAsync(
                        DeviceChatDirection.System,
                        DeviceChatEntryType.TransferStatus,
                        $"对方已同意{transferKindLabel}传输请求。",
                        "accepted");
                    await SendFileTransferPayloadAsync(target, filePath, fileInfo, requestId);
                    await SaveFileRecordAsync(
                        DeviceChatDirection.Outgoing,
                        DeviceChatEntryType.File,
                        $"{transferKindLabel}传输完成。",
                        "completed");
                    return;

                case RequestDecision.TimedOut:
                    await SaveFileRecordAsync(
                        DeviceChatDirection.System,
                        DeviceChatEntryType.TransferStatus,
                        $"对方未响应{transferKindLabel}传输请求。",
                        "timeout");
                    throw new TimeoutException("对方未在规定时间内响应。");

                default:
                    await SaveFileRecordAsync(
                        DeviceChatDirection.System,
                        DeviceChatEntryType.TransferStatus,
                        $"对方已拒绝{transferKindLabel}传输请求。",
                        "rejected");
                    return;
            }
        }
        catch (Exception ex)
        {
            FailTransferToast(requestId, ex.Message, true);
            await SaveFileRecordAsync(
                DeviceChatDirection.Outgoing,
                DeviceChatEntryType.File,
                $"{transferKindLabel}传输失败：{ex.Message}",
                "failed");
            throw;
        }
    }

    private async Task SendFileTransferPayloadAsync(DeviceModel target, string filePath, FileInfo fileInfo, string requestId)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var fileMeta = CreatePacketMetadata(
            type: PacketTypes.FileTransfer,
            requestId: requestId,
            fileName: fileInfo.Name,
            size: fileInfo.Length);
        var targetName = GetDeviceDisplayName(target);
        StartTransferToast(requestId, true, fileInfo.Name, fileInfo.Length, targetName);

        var onProgress = CreateTransferProgressHandler(
            requestId,
            isSending: true,
            fileName: fileInfo.Name,
            totalBytes: fileInfo.Length,
            remoteName: targetName,
            peer: target);

        await SendStreamInternalAsync(target, fs, JsonSerializer.Serialize(fileMeta), onProgress);
        CompleteTransferToast(requestId, true, fileInfo.Name, fileInfo.Length, targetName);
    }

    public async Task RespondToFileRequestAsync(DeviceModel target, string requestId, bool accepted, string? savePath = null)
    {
        await _fileTransferService.RespondToFileRequestAsync(requestId, accepted, savePath);
    }

    public async Task SendStreamAsync(DeviceModel target, Stream stream, string? metaData = null)
    {
        await SendStreamInternalAsync(target, stream, metaData);
    }

    private async Task SendStreamInternalAsync(DeviceModel target, Stream stream, string? metaData = null,
        Action<long>? onProgress = null)
    {
        TryDeserializePacketMetadata(metaData, out var packet);
        packet = EnsureOutgoingPacketIdentity(packet);

        try
        {
            await _transportService.SendAsync(target, packet, stream, onProgress);
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(packet.RequestId))
            {
                NotifyTransferInterrupted(packet.RequestId, $"发送失败：{ex.Message}", true);
            }

            throw;
        }
    }

    private PacketMetadata EnsureOutgoingPacketIdentity(PacketMetadata packet)
    {
        if (packet.SenderPort <= 0)
        {
            packet.SenderPort = GetAdvertisedPort();
        }

        if (string.IsNullOrWhiteSpace(packet.SenderId))
        {
            packet.SenderId = _myId.ToString();
        }

        if (string.IsNullOrWhiteSpace(packet.SenderName))
        {
            packet.SenderName = GetLocalDisplayName();
        }

        return packet;
    }

    private async Task CopyStreamWithTimeoutAsync(Stream source, Stream destination, TimeSpan timeout, int bufferSize = 8192,
        Action<long>? onProgress = null)
    {
        var buffer = new byte[bufferSize];
        int bytesRead;
        using var cts = new CancellationTokenSource();
        
        while (true)
        {
            try
            {
                cts.CancelAfter(timeout);
                bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                if (bytesRead == 0) break;
                
                cts.CancelAfter(timeout);
                await destination.WriteAsync(buffer, 0, bytesRead, cts.Token);
                onProgress?.Invoke(bytesRead);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("数据传输超时。");
            }
        }
    }

    private async Task DispatchPacketAsync(PacketMetadata packet, Stream dataStream, DeviceModel sender)
    {
        System.Diagnostics.Debug.WriteLine($"[Dispatch] Processing packet Type={packet.Type}, ID={packet.RequestId}");
        await _packetRouter.DispatchAsync(packet, dataStream, sender);
    }

    public async Task HandleMessagePacketAsync(PacketContext context)
    {
        await DrainRemainingDataAsync(context.DataStream);
        await SaveChatRecordAsync(
            context.Sender,
            DeviceChatDirection.Incoming,
            DeviceChatEntryType.Text,
            content: context.Packet.Content,
            status: "received");
        PublishMessageReceived(context.Sender, context.Packet.Content);
    }

    public async Task HandleClipboardTextPacketAsync(PacketContext context)
    {
        await DrainRemainingDataAsync(context.DataStream);
        PublishClipboardReceived(context.Sender, context.Packet.Content);
        _clipboardSyncService.ApplyIncomingClipboardText(context.Sender, context.Packet.Content);
    }

    public async Task HandleClipboardSyncRequestPacketAsync(PacketContext context)
    {
        await DrainRemainingDataAsync(context.DataStream);
        await _clipboardSyncService.HandleIncomingRequestAsync(
            context.Packet,
            context.Sender,
            PromptIncomingClipboardSyncRequestAsync);
    }

    public async Task HandleClipboardSyncResponsePacketAsync(PacketContext context)
    {
        await DrainRemainingDataAsync(context.DataStream);
        await _clipboardSyncService.HandleIncomingResponseAsync(context.Packet, context.Sender);
    }

    public async Task HandleFileRequestPacketAsync(PacketContext context)
    {
        await DrainRemainingDataAsync(context.DataStream);
        await _fileTransferService.HandleIncomingFileRequestAsync(context.Packet, context.Sender);
    }

    public async Task HandleFileResponsePacketAsync(PacketContext context)
    {
        await DrainRemainingDataAsync(context.DataStream);
        await _fileTransferService.HandleIncomingFileResponseAsync(context.Packet, context.Sender);
    }

    public async Task HandleFileTransferPacketAsync(PacketContext context)
    {
        if (await _fileTransferService.HandleIncomingFileTransferAsync(context.Packet, context.DataStream, context.Sender))
        {
            return;
        }

        await HandleUnknownPacketAsync(context);
    }

    public Task HandleLegacyPacketAsync(PacketContext context)
    {
        return HandleUnknownPacketAsync(context);
    }

    public async Task HandleUnknownPacketAsync(PacketContext context)
    {
        if (await _fileTransferService.HandleIncomingFileTransferAsync(context.Packet, context.DataStream, context.Sender))
        {
            return;
        }

        await DispatchGenericStreamAsync(context.Packet, context.DataStream, context.Sender);
    }

    private async Task HandleIncomingFileTransferToPendingPathAsync(
        PacketMetadata packet,
        Stream dataStream,
        DeviceModel sender,
        string savePath)
    {
        bool success = false;
        var senderName = GetDeviceDisplayName(sender);
        var shouldShowExternalToast = ShouldShowExternalToastForSender(sender);
        if (shouldShowExternalToast)
        {
            StartTransferToast(packet.RequestId, false, packet.FileName, packet.Size, senderName);
        }

        var onProgress = CreateTransferProgressHandler(
            packet.RequestId,
            isSending: false,
            fileName: packet.FileName,
            totalBytes: packet.Size,
            remoteName: senderName,
            peer: sender);

        try
        {
            // Stream directly to file
            await using var fs = new FileStream(savePath, FileMode.Create, FileAccess.Write);
            await CopyStreamWithTimeoutAsync(dataStream, fs, TimeSpan.FromSeconds(10), onProgress: onProgress);

            if (packet.Size > 0 && fs.Length != packet.Size)
            {
                throw new IOException($"文件大小不匹配，期望 {packet.Size} 字节，实际 {fs.Length} 字节。");
            }

            success = true;
            if (shouldShowExternalToast)
            {
                CompleteTransferToast(packet.RequestId, false, packet.FileName, packet.Size, senderName);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Dispatch] File save error: {ex}");
            NotifyTransferInterrupted(packet.RequestId, $"接收失败：{ex.Message}", false);
            await SaveChatRecordAsync(
                sender,
                DeviceChatDirection.Incoming,
                DeviceChatEntryType.File,
                content: $"文件接收失败：{ex.Message}",
                fileName: packet.FileName,
                filePath: savePath,
                fileSize: packet.Size,
                requestId: packet.RequestId,
                status: "failed");
            try
            {
                File.Delete(savePath);
            }
            catch
            {
            }
        }

        if (!success)
        {
            return;
        }

        await SaveChatRecordAsync(
            sender,
            DeviceChatDirection.Incoming,
            DeviceChatEntryType.File,
            content: "文件接收完成。",
            fileName: packet.FileName,
            filePath: savePath,
            fileSize: packet.Size,
            requestId: packet.RequestId,
            status: "completed");
        PublishFileTransferCompleted(sender, packet.RequestId, packet.FileName, packet.Size, savePath, isSending: false);

        if (ShouldShowExternalToastForSender(sender))
        {
            ShowIncomingFileSavedToast(sender, savePath);
        }

        try
        {
            using var fsRead = new FileStream(savePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            StreamReceived?.Invoke(this, new DeviceStreamReceivedEventArgs(
                sender,
                fsRead,
                JsonSerializer.Serialize(packet),
                savePath));
        }
        catch
        {
        }
    }

    private async Task<bool> TryHandleManualFileTransferAsync(PacketMetadata packet, Stream dataStream, DeviceModel sender)
    {
        if (!string.Equals(packet.Type, PacketTypes.FileTransfer, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suggestedName = string.IsNullOrWhiteSpace(packet.FileName)
            ? "接收文件"
            : Path.GetFileName(packet.FileName);
        var manualSavePath = await PickSaveFilePathAsync("\u4fdd\u5b58\u63a5\u6536\u5230\u7684\u6587\u4ef6", suggestedName);
        if (string.IsNullOrWhiteSpace(manualSavePath))
        {
            await DrainRemainingDataAsync(dataStream);
            return true;
        }

        try
        {
            await using var fs = new FileStream(manualSavePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await CopyStreamWithTimeoutAsync(dataStream, fs, TimeSpan.FromSeconds(10));
            await SaveChatRecordAsync(
                sender,
                DeviceChatDirection.Incoming,
                DeviceChatEntryType.File,
                content: "文件接收完成。",
                fileName: packet.FileName,
                filePath: manualSavePath,
                fileSize: packet.Size,
                requestId: packet.RequestId,
                status: "completed");
            PublishFileTransferCompleted(
                sender,
                packet.RequestId,
                packet.FileName,
                packet.Size,
                manualSavePath,
                isSending: false);
            if (ShouldShowExternalToastForSender(sender))
            {
                ShowIncomingFileSavedToast(sender, manualSavePath);
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Dispatch] Save manual stream error: {ex}");
            await SaveChatRecordAsync(
                sender,
                DeviceChatDirection.Incoming,
                DeviceChatEntryType.File,
                content: $"文件接收失败：{ex.Message}",
                fileName: packet.FileName,
                filePath: manualSavePath,
                fileSize: packet.Size,
                requestId: packet.RequestId,
                status: "failed");
            try
            {
                File.Delete(manualSavePath);
            }
            catch
            {
            }

            NotifyTransferInterrupted(packet.RequestId, $"接收失败：{ex.Message}", false);
            return true;
        }
    }

    private async Task DispatchGenericStreamAsync(PacketMetadata packet, Stream dataStream, DeviceModel sender)
    {
        System.Diagnostics.Debug.WriteLine($"[Dispatch] Handling Stream for {packet.Type}");
        // For file transfer (without pending path) or unknown types, we buffer if needed.
        Stream resultStream = dataStream;
        if (!dataStream.CanSeek)
        {
            System.Diagnostics.Debug.WriteLine($"[Dispatch] Buffering stream...");
            // Warning: For large files this causes high memory usage.
            // Users should use Request/Response flow with set path.
            var ms = new MemoryStream();
            await dataStream.CopyToAsync(ms); // Async copy
            ms.Position = 0;
            resultStream = ms;
            System.Diagnostics.Debug.WriteLine($"[Dispatch] Buffered {ms.Length} bytes.");
        }
        else if (dataStream.Position != 0)
        {
            dataStream.Position = 0;
        }

        System.Diagnostics.Debug.WriteLine($"[Dispatch] Invoking StreamReceived event...");
        StreamReceived?.Invoke(this, new DeviceStreamReceivedEventArgs(
            sender,
            resultStream,
            JsonSerializer.Serialize(packet)));
    }

    private static async Task DrainRemainingDataAsync(Stream stream)
    {
        if (!stream.CanRead)
        {
            return;
        }

        var buffer = new byte[8192];
        while (await stream.ReadAsync(buffer, 0, buffer.Length) > 0)
        {
        }
    }

    private static bool ShouldReplaceDiscoveredAddress(IPAddress currentAddress, IPAddress candidateAddress)
    {
        if (currentAddress.Equals(candidateAddress))
        {
            return false;
        }

        var currentFamily = currentAddress.AddressFamily;
        var candidateFamily = candidateAddress.AddressFamily;

        if (currentFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
            candidateFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return false;
        }

        if (currentFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 &&
            candidateFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return true;
        }

        return true;
    }

    private static string GetDeviceDisplayName(DeviceModel? device)
    {
        if (device is null)
        {
            return "未知设备";
        }

        if (!string.IsNullOrWhiteSpace(device.CustomName))
        {
            return device.CustomName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(device.Name))
        {
            return device.Name.Trim();
        }

        return device.Address.ToString();
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

        return $"{value:0.##} {units[unitIndex]}";
    }

    private ConcurrentDictionary<string, IToastProgressHandle> GetTransferToastMap(bool isSending)
    {
        return isSending ? _sendingTransferToasts : _receivingTransferToasts;
    }

    private static (string Action, string Direction, string Title) GetTransferToastText(bool isSending, bool completed)
    {
        if (completed)
        {
            return isSending ? ("已发送", "到", "文件发送") : ("已接收", "从", "文件接收");
        }

        return isSending ? ("发送", "到", "文件发送") : ("接收", "从", "文件接收");
    }

    private void StartTransferToast(string requestId, bool isSending, string fileName, long totalBytes, string remoteName)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        var (action, direction, title) = GetTransferToastText(isSending, completed: false);
        var detail = totalBytes > 0
            ? $"{FormatFileSize(0)} / {FormatFileSize(totalBytes)}"
            : "准备中...";
        var handle = _notificationService.ShowProgress(
            title,
            $"{action} {fileName} {direction} {remoteName} ({detail})",
            NotificationType.Information,
            initialProgress: 0,
            isIndeterminate: totalBytes <= 0);

        GetTransferToastMap(isSending)[requestId] = handle;
    }

    private void UpdateTransferToastProgress(string requestId, bool isSending, string fileName, long transferredBytes,
        long totalBytes, string remoteName)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        var map = GetTransferToastMap(isSending);
        if (!map.TryGetValue(requestId, out var handle))
        {
            return;
        }

        var (action, direction, _) = GetTransferToastText(isSending, completed: false);
        if (totalBytes > 0)
        {
            var progress = Math.Min(100, transferredBytes * 100d / totalBytes);
            handle.Update(
                progress: progress,
                text:
                $"{action} {fileName} {direction} {remoteName} ({FormatFileSize(transferredBytes)} / {FormatFileSize(totalBytes)})");
            return;
        }

        handle.Update(
            text: $"{action} {fileName} {direction} {remoteName} ({FormatFileSize(transferredBytes)})",
            isIndeterminate: true);
    }

    private void CompleteTransferToast(string requestId, bool isSending, string fileName, long totalBytes,
        string remoteName)
    {
        var map = GetTransferToastMap(isSending);
        if (!map.TryRemove(requestId, out var handle))
        {
            return;
        }

        var (action, direction, _) = GetTransferToastText(isSending, completed: true);
        var sizeText = totalBytes > 0 ? $" ({FormatFileSize(totalBytes)})" : string.Empty;
        handle.Complete($"{action} {fileName}{sizeText} {direction} {remoteName}");
    }

    private void FailTransferToast(string requestId, string reason, bool isSending)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        var map = GetTransferToastMap(isSending);
        if (!map.TryRemove(requestId, out var handle))
        {
            return;
        }

        handle.Fail($"传输中断：{reason}");
    }
    
    public void Dispose()
    {
        StopDiscovery();
        _fileTransferService.FileRequestReceived -= OnFileTransferRequestReceived;
        _fileTransferService.TransferProgress -= OnFileTransferProgress;
        _fileTransferService.TransferCompleted -= OnFileTransferCompleted;
        _fileTransferService.TransferInterrupted -= OnFileTransferInterrupted;
        _fileTransferService.StreamReceived -= OnFileTransferStreamReceived;
        _clipboardSyncService.StateChanged -= OnClipboardSyncStateChanged;
        _clipboardSyncService.Authorized -= OnClipboardSyncAuthorized;
        _transportService.PacketReceived -= OnTransportPacketReceived;
        _discoveryService.DeviceDiscovered -= OnDeviceDiscovered;
        _discoveryService.DeviceUpdated -= OnDeviceUpdated;
        _discoveryService.DeviceLost -= OnDeviceLost;
        _fileTransferService.Dispose();
        _clipboardSyncService.Dispose();
        _transportService.Dispose();
        _discoveryService.Dispose();
    }
}
