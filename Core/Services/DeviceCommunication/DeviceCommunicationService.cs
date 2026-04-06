using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Core.Services;
using Core.Services.Config;
using PluginCore;
using Serilog;

namespace Core.Services.DeviceCommunication;

public class DeviceCommunicationService : IDeviceCommunication, IDisposable
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<DeviceCommunicationService>();
    private const int ClipboardPollIntervalMs = 800;
    private static readonly TimeSpan ClipboardEchoSuppressionWindow = TimeSpan.FromSeconds(2);
    private const int DiscoveryPort = 53535;
    private const string MulticastAddressV4 = "239.255.255.250";
    private const string MulticastAddressV6 = "ff02::1";
    private const string ProtocolId = "kitopia-stream";
    private const int DiscoveryIpv4Ttl = 1;
    private static readonly TimeSpan DiscoveryBroadcastInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DiscoveryCleanupInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DiscoveryStaleTimeout = TimeSpan.FromSeconds(20);
    
    private readonly Guid _myId = LoadOrCreateDeviceIdFromConfig();
    
    private UdpClient? _discoveryUdpClientV4;
    private UdpClient? _discoveryUdpClientV6;
    private CancellationTokenSource? _discoveryCts;
    private QuicListener? _quicListener;
    private UdpClient? _udpDataClientV4;
    private UdpClient? _udpDataClientV6;
    private int _quicPort;
    private int _udpDataPort;
    private int _advertisedPort;
    private bool _supportsQuicTransport;
    private X509Certificate2? _serverCert;

    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pendingFileRequests = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pendingClipboardSyncRequests = new();
    private readonly ConcurrentDictionary<Guid, UdpReassemblySession> _udpSessions = new();
    private readonly ConcurrentDictionary<string, string> _pendingDownloads = new(); // RequestId -> SavePath
    private readonly ConcurrentDictionary<string, IncomingFileRequestContext> _pendingIncomingFileRequests = new();
    private readonly ConcurrentDictionary<string, bool> _discoveredDeviceQuicCapabilities = new(); // DeviceId -> SupportsQuic
    private readonly ConcurrentDictionary<string, IToastProgressHandle> _sendingTransferToasts = new();
    private readonly ConcurrentDictionary<string, IToastProgressHandle> _receivingTransferToasts = new();
    private static readonly TimeSpan TransferToastUpdateInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan ClipboardSyncRequestTimeout = TimeSpan.FromSeconds(30);
    private const int MaxBufferedMessages = 100;
    private readonly object _messageReceivedLock = new();
    private readonly Queue<DeviceMessageReceivedEventArgs> _bufferedMessages = new();
    private EventHandler<DeviceMessageReceivedEventArgs>? _messageReceivedHandlers;
    private readonly IDeviceChatHistoryStore _chatHistoryStore;
    private readonly IClipboardService _clipboardService;
    private readonly object _clipboardSyncLock = new();
    private CancellationTokenSource? _clipboardSyncCts;
    private DeviceModel? _clipboardSyncTargetDevice;
    private string _lastSyncedClipboardText = string.Empty;
    private string _lastInboundClipboardText = string.Empty;
    private DateTime _lastInboundClipboardUtc = DateTime.MinValue;
    private int _isApplyingRemoteClipboard;
    private bool _isClipboardSyncEnabled;

    public ObservableCollection<DeviceModel> DiscoveredDevices { get; } = new();
    public bool IsClipboardSyncEnabled
    {
        get
        {
            lock (_clipboardSyncLock)
            {
                return _isClipboardSyncEnabled;
            }
        }
    }

    public DeviceModel? ClipboardSyncTargetDevice
    {
        get
        {
            lock (_clipboardSyncLock)
            {
                return _clipboardSyncTargetDevice is null ? null : CloneDeviceModel(_clipboardSyncTargetDevice);
            }
        }
    }

    public event EventHandler<DeviceStreamReceivedEventArgs>? StreamReceived;
    public event EventHandler<DeviceMessageReceivedEventArgs>? MessageReceived
    {
        add
        {
            if (value is null)
            {
                return;
            }

            List<DeviceMessageReceivedEventArgs>? bufferedMessages = null;
            lock (_messageReceivedLock)
            {
                if (_messageReceivedHandlers != null)
                {
                    foreach (var existingHandler in _messageReceivedHandlers.GetInvocationList())
                    {
                        if (existingHandler is EventHandler<DeviceMessageReceivedEventArgs> typedHandler &&
                            ShouldReplaceMessageSubscriber(typedHandler, value))
                        {
                            _messageReceivedHandlers -= typedHandler;
                        }
                    }
                }

                _messageReceivedHandlers += value;

                if (_bufferedMessages.Count > 0)
                {
                    bufferedMessages = _bufferedMessages.Select(CloneMessageEventArgs).ToList();
                    _bufferedMessages.Clear();
                }
            }

            if (bufferedMessages is null)
            {
                return;
            }

            foreach (var bufferedMessage in bufferedMessages)
            {
                try
                {
                    value(this, bufferedMessage);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Message] Replay handler error: {ex}");
                }
            }
        }
        remove
        {
            if (value is null)
            {
                return;
            }

            lock (_messageReceivedLock)
            {
                _messageReceivedHandlers -= value;
            }
        }
    }
    public event EventHandler<DeviceClipboardReceivedEventArgs>? ClipboardTextReceived;
    public event EventHandler<DeviceClipboardSyncAuthorizedEventArgs>? ClipboardSyncAuthorized;
    public event EventHandler<DeviceClipboardSyncStateChangedEventArgs>? ClipboardSyncStateChanged;
    public event EventHandler<FileTransferRequestEventArgs>? FileTransferRequested;
    public event EventHandler<TransferInterruptionEventArgs>? TransferInterrupted;

    public DeviceCommunicationService(
        IDeviceChatHistoryStore chatHistoryStore,
        IClipboardService clipboardService)
    {
        _chatHistoryStore = chatHistoryStore;
        _clipboardService = clipboardService;
        _serverCert = GenerateCertificate();
        _lastSyncedClipboardText = _clipboardService.GetText() ?? string.Empty;
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

            await _chatHistoryStore.AppendAsync(new DeviceChatMessage
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
                GetToastService()?.Show(
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
                TransferInterrupted?.Invoke(this, new TransferInterruptionEventArgs(requestId, reason, isSending));
            }
        });
    }

    private static void InvokeEventHandlersSafely<TEventArgs>(
        object sender,
        EventHandler<TEventArgs>? handlers,
        TEventArgs args,
        string errorTag)
        where TEventArgs : EventArgs
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList())
        {
            if (handler is not EventHandler<TEventArgs> typedHandler)
            {
                continue;
            }

            try
            {
                typedHandler(sender, args);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[{errorTag}] Handler error: {ex}");
            }
        }
    }

    private void PublishMessageReceived(DeviceModel sender, string message)
    {
        var args = new DeviceMessageReceivedEventArgs(sender, message);
        EventHandler<DeviceMessageReceivedEventArgs>? handlers;
        lock (_messageReceivedLock)
        {
            handlers = _messageReceivedHandlers;
            if (handlers is null)
            {
                _bufferedMessages.Enqueue(CloneMessageEventArgs(args));
                while (_bufferedMessages.Count > MaxBufferedMessages)
                {
                    _bufferedMessages.Dequeue();
                }
            }
        }

        if (handlers is null)
        {
            ShowIncomingMessageToast(args);
            return;
        }

        InvokeEventHandlersSafely(this, handlers, args, "Message");
    }

    private void PublishClipboardReceived(DeviceModel sender, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var handlers = ClipboardTextReceived;
        if (handlers is null)
        {
            return;
        }

        var args = new DeviceClipboardReceivedEventArgs(sender, text);
        InvokeEventHandlersSafely(this, handlers, args, "Clipboard");
    }

    private void PublishClipboardSyncAuthorized(DeviceModel peer, bool initiatedByPeer)
    {
        var handlers = ClipboardSyncAuthorized;
        if (handlers is null)
        {
            return;
        }

        var args = new DeviceClipboardSyncAuthorizedEventArgs(peer, initiatedByPeer);
        InvokeEventHandlersSafely(this, handlers, args, "ClipboardSync Authorization");
    }

    private void PublishClipboardSyncStateChanged(bool isEnabled, DeviceModel? target, string status)
    {
        var handlers = ClipboardSyncStateChanged;
        if (handlers is null)
        {
            return;
        }

        var clonedTarget = target is null ? null : CloneDeviceModel(target);
        var args = new DeviceClipboardSyncStateChangedEventArgs(isEnabled, clonedTarget, status);
        InvokeEventHandlersSafely(this, handlers, args, "ClipboardSync State");
    }

    private void UpdateClipboardSyncState(bool isEnabled, DeviceModel? target, string status, bool keepTargetWhenDisabled)
    {
        DeviceModel? targetSnapshot;
        lock (_clipboardSyncLock)
        {
            _isClipboardSyncEnabled = isEnabled;
            if (target is not null)
            {
                _clipboardSyncTargetDevice = CloneDeviceModel(target);
            }
            else if (!keepTargetWhenDisabled)
            {
                _clipboardSyncTargetDevice = null;
            }

            targetSnapshot = _clipboardSyncTargetDevice is null ? null : CloneDeviceModel(_clipboardSyncTargetDevice);
        }

        PublishClipboardSyncStateChanged(isEnabled, targetSnapshot, status);
    }

    private void ActivateClipboardSync(DeviceModel target, string status)
    {
        var resolvedTarget = ResolveDiscoveredDevice(target) ?? target;
        var initialClipboardText = _clipboardService.GetText() ?? string.Empty;
        bool shouldStartMonitor = false;
        CancellationToken monitorToken = default;

        lock (_clipboardSyncLock)
        {
            _isClipboardSyncEnabled = true;
            _clipboardSyncTargetDevice = CloneDeviceModel(resolvedTarget);
            _lastSyncedClipboardText = initialClipboardText;

            if (_clipboardSyncCts is null)
            {
                _clipboardSyncCts = new CancellationTokenSource();
                shouldStartMonitor = true;
                monitorToken = _clipboardSyncCts.Token;
            }
        }

        if (shouldStartMonitor)
        {
            _ = MonitorClipboardLoopAsync(monitorToken);
        }

        UpdateClipboardSyncState(true, resolvedTarget, status, keepTargetWhenDisabled: true);
    }

    private void DeactivateClipboardSync(string status, bool keepTargetWhenDisabled)
    {
        CancellationTokenSource? ctsToDispose;
        lock (_clipboardSyncLock)
        {
            _isClipboardSyncEnabled = false;
            ctsToDispose = _clipboardSyncCts;
            _clipboardSyncCts = null;
        }

        if (ctsToDispose is not null)
        {
            try
            {
                ctsToDispose.Cancel();
            }
            catch
            {
            }
            ctsToDispose.Dispose();
        }

        UpdateClipboardSyncState(false, null, status, keepTargetWhenDisabled);
    }

    private async Task MonitorClipboardLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                DeviceModel? target;
                bool enabled;
                lock (_clipboardSyncLock)
                {
                    enabled = _isClipboardSyncEnabled;
                    target = _clipboardSyncTargetDevice is null ? null : CloneDeviceModel(_clipboardSyncTargetDevice);
                }

                if (enabled && target is not null)
                {
                    var discoveredTarget = ResolveDiscoveredDevice(target);
                    if (discoveredTarget is null)
                    {
                        DeactivateClipboardSync("同步目标设备已离线，请重新选择", keepTargetWhenDisabled: false);
                        continue;
                    }

                    var isApplyingRemote = Interlocked.CompareExchange(ref _isApplyingRemoteClipboard, 0, 0) == 1;
                    if (!isApplyingRemote)
                    {
                        var currentText = _clipboardService.GetText() ?? string.Empty;
                        if (!string.IsNullOrEmpty(currentText))
                        {
                            bool shouldSend;
                            bool suppressedEcho;
                            lock (_clipboardSyncLock)
                            {
                                shouldSend = !string.Equals(currentText, _lastSyncedClipboardText, StringComparison.Ordinal);
                                suppressedEcho = shouldSend &&
                                                 string.Equals(currentText, _lastInboundClipboardText, StringComparison.Ordinal) &&
                                                 DateTime.UtcNow - _lastInboundClipboardUtc <= ClipboardEchoSuppressionWindow;
                                if (suppressedEcho)
                                {
                                    shouldSend = false;
                                }

                                if (shouldSend)
                                {
                                    _lastSyncedClipboardText = currentText;
                                }
                            }

                            if (suppressedEcho)
                            {
                                Logger.Debug(
                                    "[ClipboardSync] Suppressed echo loop for {Target} ({TargetId}): {ClipboardPreview}",
                                    GetDeviceDisplayName(discoveredTarget),
                                    discoveredTarget.Id,
                                    ToClipboardLogPreview(currentText));
                            }

                            if (shouldSend)
                            {
                                await SendClipboardTextAsync(discoveredTarget, currentText);
                            }
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
                System.Diagnostics.Debug.WriteLine($"[ClipboardSync] Monitor loop error: {ex}");
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

    private void ApplyIncomingClipboardText(DeviceModel sender, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Logger.Information(
            "[ClipboardSync] Received from {Sender} ({SenderId}): {ClipboardPreview}",
            GetDeviceDisplayName(sender),
            sender.Id,
            ToClipboardLogPreview(text));

        DeviceModel? target;
        bool enabled;
        lock (_clipboardSyncLock)
        {
            enabled = _isClipboardSyncEnabled;
            target = _clipboardSyncTargetDevice is null ? null : CloneDeviceModel(_clipboardSyncTargetDevice);
        }

        if (!enabled || target is null || !IsSameDevice(target, sender))
        {
            return;
        }

        lock (_clipboardSyncLock)
        {
            if (string.Equals(text, _lastSyncedClipboardText, StringComparison.Ordinal))
            {
                return;
            }
        }

        Interlocked.Exchange(ref _isApplyingRemoteClipboard, 1);
        try
        {
            if (!_clipboardService.SetText(text))
            {
                UpdateClipboardSyncState(true, target, "接收远端剪贴板失败", keepTargetWhenDisabled: true);
                return;
            }

            lock (_clipboardSyncLock)
            {
                _lastSyncedClipboardText = text;
                _lastInboundClipboardText = text;
                _lastInboundClipboardUtc = DateTime.UtcNow;
            }

            UpdateClipboardSyncState(true, target, $"已从 {sender.DisplayName} 同步剪贴板", keepTargetWhenDisabled: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClipboardSync] Apply incoming text error: {ex}");
            UpdateClipboardSyncState(true, target, "接收远端剪贴板失败", keepTargetWhenDisabled: true);
        }
        finally
        {
            Interlocked.Exchange(ref _isApplyingRemoteClipboard, 0);
        }
    }

    private void ShowIncomingMessageToast(DeviceMessageReceivedEventArgs args)
    {
        var senderName = GetDeviceDisplayName(args.Sender);
        _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                GetToastService()?.Show(
                    $"\u6d88\u606f\u6765\u81ea {senderName}",
                    args.Message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Message] Toast error: {ex}");
            }
        });
    }

    private async Task HandleIncomingClipboardSyncRequestAsync(PacketMetadata packet, DeviceModel sender)
    {
        if (string.IsNullOrWhiteSpace(packet.RequestId))
        {
            return;
        }

        var accepted = await PromptIncomingClipboardSyncRequestAsync(sender);
        await SendClipboardSyncResponseAsync(sender, packet.RequestId, accepted);
        if (!accepted)
        {
            return;
        }

        ActivateClipboardSync(sender, $"已同意 {GetDeviceDisplayName(sender)} 的请求，双向同步已开启");
        PublishClipboardSyncAuthorized(sender, initiatedByPeer: true);
    }

    private async Task SendClipboardSyncResponseAsync(DeviceModel target, string requestId, bool accepted)
    {
        await SendBooleanResponseAsync(target, "ClipboardSyncResp", requestId, accepted);
    }

    private async Task<bool> PromptIncomingClipboardSyncRequestAsync(DeviceModel sender)
    {
        return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                var toastService = GetToastService();
                if (toastService is null)
                {
                    return false;
                }

                var senderName = GetDeviceDisplayName(sender);
                var decisionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                toastService.Show(new ToastRequest
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

            var hasActiveChatHandler = FileTransferRequested is not null;
            if (hasActiveChatHandler)
            {
                try
                {
                    FileTransferRequested?.Invoke(this,
                        new FileTransferRequestEventArgs(packet.RequestId, packet.FileName, packet.Size,
                            CloneDeviceModel(sender)));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FileReq] Event handler error: {ex}");
                }
            }

            if (!hasActiveChatHandler)
            {
                ShowIncomingFileRequestActionToast(packet.RequestId, sender, packet.FileName, packet.Size);
            }
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
                await SendBooleanResponseAsync(sender, "FileResp", packet.RequestId, false);
            }
            catch
            {
            }
        }
    }

    private async Task<string?> PickFileToSendAsync()
    {
        return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (lifetime?.MainWindow == null)
            {
                return null;
            }

            var files = await lifetime.MainWindow.StorageProvider.OpenFilePickerAsync(
                new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "选择要发送的文件",
                    AllowMultiple = false
                });

            if (files == null || files.Count == 0)
            {
                return null;
            }

            return files[0].Path.LocalPath;
        });
    }

    private async Task<string?> PickSaveFilePathAsync(string title, string suggestedFileName)
    {
        return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
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

    private void ShowIncomingFileRequestActionToast(string requestId, DeviceModel sender, string fileName, long fileSize)
    {
        var senderName = GetDeviceDisplayName(sender);
        var displayFileName = string.IsNullOrWhiteSpace(fileName) ? "未命名文件" : fileName;
        var sizeText = fileSize > 0 ? $" ({FormatFileSize(fileSize)})" : string.Empty;
        _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                var toastService = GetToastService();
                if (toastService is null)
                {
                    return;
                }

                toastService.Show(new ToastRequest
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
                            Callback = () => _ = HandleIncomingFileRequestDecisionFromToastAsync(requestId, true)
                        },
                        new ToastAction
                        {
                            Text = "拒绝",
                            Callback = () => _ = HandleIncomingFileRequestDecisionFromToastAsync(requestId, false)
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

    private async Task HandleIncomingFileRequestDecisionFromToastAsync(string requestId, bool accepted)
    {
        if (!_pendingIncomingFileRequests.TryGetValue(requestId, out var context))
        {
            return;
        }

        string? savePath = null;
        if (accepted)
        {
            var suggestedName = string.IsNullOrWhiteSpace(context.FileName) ? "接收文件" : context.FileName;
            savePath = await PickSaveFilePathAsync("保存文件", suggestedName);
            if (string.IsNullOrWhiteSpace(savePath))
            {
                return;
            }
        }

        try
        {
            await RespondToFileRequestAsync(context.Sender, requestId, accepted, savePath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FileReq] Toast decision error: {ex}");
            _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    GetToastService()?.Show("处理文件请求失败", ex.Message, NotificationType.Error);
                }
                catch
                {
                }
            });
        }
    }

    private void ShowIncomingFileSavedToast(DeviceModel sender, string savedPath)
    {
        var senderName = GetDeviceDisplayName(sender);
        _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                GetToastService()?.Show(
                    "\u6587\u4ef6\u63a5\u6536\u6210\u529f",
                    $"\u6765\u81ea {senderName} \u7684\u6587\u4ef6\u5df2\u4fdd\u5b58\u81f3: {savedPath}",
                    NotificationType.Success);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FileTransfer] Success toast error: {ex}");
            }
        });
    }

    private static bool ShouldReplaceMessageSubscriber(Delegate existingHandler, Delegate newHandler)
    {
        if (existingHandler.Method != newHandler.Method)
        {
            return false;
        }

        var existingTargetType = existingHandler.Target?.GetType();
        var newTargetType = newHandler.Target?.GetType();
        if (existingTargetType is null || newTargetType is null)
        {
            return false;
        }

        return existingTargetType == newTargetType;
    }

    private static DeviceMessageReceivedEventArgs CloneMessageEventArgs(DeviceMessageReceivedEventArgs source)
    {
        return new DeviceMessageReceivedEventArgs(CloneDeviceModel(source.Sender), source.Message);
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

    private static string ToClipboardLogPreview(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "<empty>";
        }

        var normalized = text.Replace("\r", "\\r").Replace("\n", "\\n");
        const int maxLen = 120;
        if (normalized.Length <= maxLen)
        {
            return normalized;
        }

        return normalized[..maxLen] + "...";
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
        ConcurrentDictionary<string, TaskCompletionSource<bool>> pendingRequests,
        TimeSpan timeout,
        string duplicateRequestError,
        Func<Task>? onRequestSent = null)
    {
        if (string.IsNullOrWhiteSpace(requestMeta.RequestId))
        {
            throw new InvalidOperationException("请求标识不能为空。");
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pendingRequests.TryAdd(requestMeta.RequestId, tcs))
        {
            throw new InvalidOperationException(duplicateRequestError);
        }

        try
        {
            await SendPacketMetadataAsync(target, requestMeta);
            if (onRequestSent is not null)
            {
                await onRequestSent();
            }

            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
            if (completedTask != tcs.Task)
            {
                return RequestDecision.TimedOut;
            }

            return tcs.Task.Result ? RequestDecision.Accepted : RequestDecision.Rejected;
        }
        finally
        {
            pendingRequests.TryRemove(requestMeta.RequestId, out _);
        }
    }

    private static void ResolvePendingBooleanRequest(
        ConcurrentDictionary<string, TaskCompletionSource<bool>> pendingRequests,
        string requestId,
        bool accepted)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        if (pendingRequests.TryGetValue(requestId, out var request))
        {
            request.TrySetResult(accepted);
        }
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
        string remoteName)
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
        };
    }

    private int GetAdvertisedPort()
    {
        return _advertisedPort > 0 ? _advertisedPort : _quicPort;
    }

    public void StartDiscovery()
    {
        StopDiscovery();
        _discoveryCts = new CancellationTokenSource();

        // 1. Start QUIC Listener
        _supportsQuicTransport = StartQuicListener();
        
        // 2. Start UDP Data Listener
        StartUdpDataListener(_supportsQuicTransport);

        // 3. Start Discovery Broadcast and Listen
        Task.Run(() => DiscoveryLoop(_discoveryCts.Token));
        Task.Run(() => BroadcastLoop(_discoveryCts.Token));
        Task.Run(() => CleanupLoop(_discoveryCts.Token));
    }

    public void StopDiscovery()
    {
        _discoveryCts?.Cancel();
        _discoveryCts?.Dispose();
        _discoveryUdpClientV4?.Close();
        _discoveryUdpClientV6?.Close();
        _quicListener?.DisposeAsync().AsTask().Wait();
        _udpDataClientV4?.Close();
        _udpDataClientV6?.Close();
        _discoveryUdpClientV4 = null;
        _discoveryUdpClientV6 = null;
        _quicListener = null;
        _udpDataClientV4 = null;
        _udpDataClientV6 = null;
        _discoveryCts = null;
        _supportsQuicTransport = false;
        _quicPort = 0;
        _udpDataPort = 0;
        _advertisedPort = 0;
        _discoveredDeviceQuicCapabilities.Clear();
        DeactivateClipboardSync("实时同步剪贴板已关闭", keepTargetWhenDisabled: false);

        foreach (var pending in _pendingClipboardSyncRequests.Values)
        {
            pending.TrySetResult(false);
        }
        _pendingClipboardSyncRequests.Clear();
        _pendingIncomingFileRequests.Clear();
    }

    public async Task SendMessageAsync(DeviceModel target, string message)
    {
        var targetName = GetDeviceDisplayName(target);
        try
        {
            var meta = CreatePacketMetadata(type: "Message", content: message);
            await SendPacketMetadataAsync(target, meta);
            await SaveChatRecordAsync(
                target,
                DeviceChatDirection.Outgoing,
                DeviceChatEntryType.Text,
                content: message,
                status: "sent");
            _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                GetToastService()?.Show(
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
                GetToastService()?.Show(
                    "\u6d88\u606f\u53d1\u9001\u5931\u8d25",
                    $"\u53d1\u9001\u5230 {targetName} \u65f6\u51fa\u9519: {ex.Message}",
                    NotificationType.Error);
            });
            throw;
        }
    }

    public async Task SendClipboardTextAsync(DeviceModel target, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var meta = CreatePacketMetadata(type: "ClipboardText", content: text);
        await SendPacketMetadataAsync(target, meta);
        Logger.Information(
            "[ClipboardSync] Sent to {Target} ({TargetId}): {ClipboardPreview}",
            GetDeviceDisplayName(target),
            target.Id,
            ToClipboardLogPreview(text));
    }

    public async Task<bool> RequestClipboardSyncAsync(DeviceModel target)
    {
        var requestMeta = CreatePacketMetadata(
            type: "ClipboardSyncReq",
            requestId: Guid.NewGuid().ToString("N"));
        var decision = await SendBooleanRequestAsync(
            target,
            requestMeta,
            _pendingClipboardSyncRequests,
            ClipboardSyncRequestTimeout,
            "无法跟踪剪贴板同步请求。");
        return decision == RequestDecision.Accepted;
    }

    public async Task<bool> EnableClipboardSyncAsync(DeviceModel target)
    {
        var resolvedTarget = ResolveDiscoveredDevice(target) ?? target;
        UpdateClipboardSyncState(
            isEnabled: false,
            target: resolvedTarget,
            status: $"等待 {GetDeviceDisplayName(resolvedTarget)} 同意同步请求...",
            keepTargetWhenDisabled: true);

        var accepted = await RequestClipboardSyncAsync(resolvedTarget);
        if (!accepted)
        {
            UpdateClipboardSyncState(
                isEnabled: false,
                target: resolvedTarget,
                status: "对方未同意同步请求或请求超时",
                keepTargetWhenDisabled: true);
            return false;
        }

        if (!IsClipboardSyncEnabled || ClipboardSyncTargetDevice is not { } current || !IsSameDevice(current, resolvedTarget))
        {
            ActivateClipboardSync(resolvedTarget, $"已与 {GetDeviceDisplayName(resolvedTarget)} 建立双向剪贴板同步");
        }

        return true;
    }

    public void DisableClipboardSync()
    {
        DeactivateClipboardSync("实时同步剪贴板已关闭", keepTargetWhenDisabled: false);
    }

    public async Task RequestFileTransferAsync(DeviceModel target, string? filePath = null)
    {
        filePath = string.IsNullOrWhiteSpace(filePath) ? await PickFileToSendAsync() : filePath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        if (!File.Exists(filePath)) throw new FileNotFoundException(filePath);

        var fileInfo = new FileInfo(filePath);
        var requestId = Guid.NewGuid().ToString();
        var requestMeta = CreatePacketMetadata(
            type: "FileReq",
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
                _pendingFileRequests,
                TimeSpan.FromMinutes(1),
                "无法跟踪文件传输请求。",
                onRequestSent: () => SaveFileRecordAsync(
                    DeviceChatDirection.Outgoing,
                    DeviceChatEntryType.FileRequest,
                    "已发起文件传输请求。",
                    "requested"));

            switch (decision)
            {
                case RequestDecision.Accepted:
                    await SaveFileRecordAsync(
                        DeviceChatDirection.System,
                        DeviceChatEntryType.TransferStatus,
                        "对方已同意文件传输请求。",
                        "accepted");
                    await SendFileTransferPayloadAsync(target, filePath, fileInfo, requestId);
                    await SaveFileRecordAsync(
                        DeviceChatDirection.Outgoing,
                        DeviceChatEntryType.File,
                        "文件传输完成。",
                        "completed");
                    return;

                case RequestDecision.TimedOut:
                    await SaveFileRecordAsync(
                        DeviceChatDirection.System,
                        DeviceChatEntryType.TransferStatus,
                        "对方未响应文件传输请求。",
                        "timeout");
                    throw new TimeoutException("对方未在规定时间内响应。");

                default:
                    await SaveFileRecordAsync(
                        DeviceChatDirection.System,
                        DeviceChatEntryType.TransferStatus,
                        "对方已拒绝文件传输请求。",
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
                $"文件传输失败：{ex.Message}",
                "failed");
            throw;
        }
    }

    private async Task SendFileTransferPayloadAsync(DeviceModel target, string filePath, FileInfo fileInfo, string requestId)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var fileMeta = CreatePacketMetadata(
            type: "FileTransfer",
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
            remoteName: targetName);

        await SendStreamInternalAsync(target, fs, JsonSerializer.Serialize(fileMeta), onProgress);
        CompleteTransferToast(requestId, true, fileInfo.Name, fileInfo.Length, targetName);
    }

    public async Task RespondToFileRequestAsync(DeviceModel target, string requestId, bool accepted, string? savePath = null)
    {
        if (!_pendingIncomingFileRequests.TryGetValue(requestId, out var requestContext))
        {
            return;
        }

        if (accepted && !string.IsNullOrEmpty(savePath))
        {
            _pendingDownloads[requestId] = savePath;
        }

        await SendBooleanResponseAsync(requestContext.Sender, "FileResp", requestId, accepted);

        if (_pendingIncomingFileRequests.TryRemove(requestId, out _))
        {
            var statusContent = accepted
                ? "已同意接收文件请求。"
                : "已拒绝文件请求。";
            await SaveChatRecordAsync(
                requestContext.Sender,
                DeviceChatDirection.System,
                DeviceChatEntryType.TransferStatus,
                content: statusContent,
                fileName: requestContext.FileName,
                filePath: savePath ?? string.Empty,
                fileSize: requestContext.FileSize,
                requestId: requestId,
                status: accepted ? "accepted" : "rejected");
        }
    }

    public async Task SendStreamAsync(DeviceModel target, Stream stream, string? metaData = null)
    {
        await SendStreamInternalAsync(target, stream, metaData);
    }

    private async Task SendStreamInternalAsync(DeviceModel target, Stream stream, string? metaData = null,
        Action<long>? onProgress = null)
    {
        // Prioritize QUIC
        if (ShouldTryQuic(target) && await TrySendQuicAsync(target, stream, metaData, onProgress))
            return;

        // Check if we should fallback to UDP
        // Fallback is allowed, but we proceed with caution regarding speed.
        if (!string.IsNullOrEmpty(metaData))
        {
             // Optional: Log or notify about fallback if needed
        }

        // Fallback to UDP
        try
        {
            await SendUdpAsync(target, stream, metaData, onProgress);
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrEmpty(metaData))
            {
                try
                {
                    var meta = JsonSerializer.Deserialize<PacketMetadata>(metaData, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (meta != null && !string.IsNullOrEmpty(meta.RequestId))
                    {
                        NotifyTransferInterrupted(meta.RequestId, $"发送失败：{ex.Message}", true);
                    }
                }
                catch { }
            }
            throw;
        }
    }

    private bool ShouldTryQuic(DeviceModel target)
    {
        if (!QuicConnection.IsSupported) return false;
        if (target.Port <= 0) return false;

        if (!string.IsNullOrWhiteSpace(target.Id) &&
            _discoveredDeviceQuicCapabilities.TryGetValue(target.Id, out var supportsQuic) &&
            !supportsQuic)
        {
            return false;
        }

        return true;
    }

    private async Task<bool> TrySendQuicAsync(DeviceModel target, Stream stream, string? metaData,
        Action<long>? onProgress = null)
    {
        try
        {
            if (!QuicConnection.IsSupported) return false;

            var endPoint = CreateTargetEndPoint(target.Address, target.Port);
            // Assuming target.Port is the QUIC port.

            var connectionOptions = new QuicClientConnectionOptions
            {
                RemoteEndPoint = endPoint,
                DefaultStreamErrorCode = 0,
                DefaultCloseErrorCode = 0,
                ClientAuthenticationOptions = new SslClientAuthenticationOptions
                {
                    ApplicationProtocols = new List<SslApplicationProtocol> { new SslApplicationProtocol(ProtocolId) },
                    RemoteCertificateValidationCallback = (_, _, _, _) => true // Self-signed certs
                }
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await using var connection = await QuicConnection.ConnectAsync(connectionOptions, cts.Token);
            
            using var streamCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await using var quicStream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, streamCts.Token);

            // Send Metadata
            await WriteMetaDataAsync(quicStream, metaData, TimeSpan.FromSeconds(2));
            
            // Send Data with timeout
            await CopyStreamWithTimeoutAsync(stream, quicStream, TimeSpan.FromSeconds(10), onProgress: onProgress);
            
            return true;
        }
        catch (Exception)
        {
            return false;
        }
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

    private async Task SendUdpAsync(DeviceModel target, Stream stream, string? metaData, Action<long>? onProgress = null)
    {
        // Simple UDP impl with chunking and reassembly support
        var targetEndPoint = CreateTargetEndPoint(target.Address, target.Port + 1);
        using var tempClient = targetEndPoint.AddressFamily == AddressFamily.InterNetworkV6
            ? new UdpClient(AddressFamily.InterNetworkV6)
            : new UdpClient(AddressFamily.InterNetwork);

        var sessionId = Guid.NewGuid();
        
        // 1. Send Metadata (Offset 0, Type 0)
        var metaBytes = System.Text.Encoding.UTF8.GetBytes(metaData ?? string.Empty);
        await SendUdpPacket(tempClient, targetEndPoint, sessionId, 0, 0, metaBytes, false);
        
        // 2. Send Data
        const int ChunkSize = 4096; // Safe payload size
        var buffer = new byte[ChunkSize];
        int read;
        long offset = 0;
        int packetCount = 0;

        if (stream.CanSeek) stream.Position = 0;

        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await SendUdpPacket(tempClient, targetEndPoint, sessionId, offset, 1, buffer.AsSpan(0, read).ToArray(), false);
            offset += read;
            onProgress?.Invoke(read);
            
            // Throttle: Reduce to 1ms delay every 10 packets to improve speed
            packetCount++;
            if (packetCount % 10 == 0) await Task.Delay(1); 
        }

        // Send End Packet
        await SendUdpPacket(tempClient, targetEndPoint, sessionId, offset, 1, Array.Empty<byte>(), true);
    }
    
    private async Task SendUdpPacket(UdpClient client, IPEndPoint target, Guid sessionId, long offset, byte type, byte[] data, bool isEnd)
    {
        // Header: [SessionId 16][Offset 8][Type 1][IsEnd 1] = 26 bytes
        var packet = new byte[26 + data.Length];
        sessionId.TryWriteBytes(packet.AsSpan(0, 16));
        BitConverter.TryWriteBytes(packet.AsSpan(16, 8), offset);
        packet[24] = type;
        packet[25] = isEnd ? (byte)1 : (byte)0;
        
        if (data.Length > 0)
        {
            data.CopyTo(packet.AsSpan(26));
        }
        
        await client.SendAsync(packet, packet.Length, target);
    }

    private async Task WriteMetaDataAsync(Stream stream, string? metaData, TimeSpan timeout)
    {
        var json = metaData ?? string.Empty;
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var lenBytes = BitConverter.GetBytes(bytes.Length);
        
        using var cts = new CancellationTokenSource();
        
        try
        {
            cts.CancelAfter(timeout);
            await stream.WriteAsync(lenBytes, 0, lenBytes.Length, cts.Token);
            
            cts.CancelAfter(timeout);
            await stream.WriteAsync(bytes, 0, bytes.Length, cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("写入元数据超时。");
        }
    }
    
    private bool StartQuicListener()
    {
        if (!QuicListener.IsSupported) return false;

        try
        {
            var options = new QuicListenerOptions
            {
                ApplicationProtocols = new List<SslApplicationProtocol> { new SslApplicationProtocol(ProtocolId) },
                ListenEndPoint = new IPEndPoint(IPAddress.IPv6Any, 0), // Random port (prefer dual-stack)
                ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(new QuicServerConnectionOptions
                {
                    DefaultStreamErrorCode = 0,
                    DefaultCloseErrorCode = 0,
                    ServerAuthenticationOptions = new SslServerAuthenticationOptions
                    {
                        ApplicationProtocols = new List<SslApplicationProtocol> { new SslApplicationProtocol(ProtocolId) },
                        ServerCertificate = _serverCert
                    }
                })
            };

            _quicListener = QuicListener.ListenAsync(options).AsTask().GetAwaiter().GetResult();
            _quicPort = _quicListener.LocalEndPoint.Port;
            _advertisedPort = _quicPort;
            _ = AcceptConnectionsAsync(_quicListener, _discoveryCts!.Token);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"QUIC listener disabled: {ex.Message}");
            _quicListener = null;
            _quicPort = 0;
            return false;
        }
    }
    
    private async Task AcceptConnectionsAsync(QuicListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var connection = await listener.AcceptConnectionAsync(token);
                _ = HandleQuicConnectionAsync(connection);
            }
            catch { break; }
        }
    }

    private async Task DispatchPacketAsync(PacketMetadata packet, Stream dataStream, DeviceModel sender)
    {
        System.Diagnostics.Debug.WriteLine($"[Dispatch] Processing packet Type={packet.Type}, ID={packet.RequestId}");
        // Dispatch
        switch (packet.Type)
        {
            case "Message":
                await DrainRemainingDataAsync(dataStream);
                await SaveChatRecordAsync(
                    sender,
                    DeviceChatDirection.Incoming,
                    DeviceChatEntryType.Text,
                    content: packet.Content,
                    status: "received");
                PublishMessageReceived(sender, packet.Content);
                break;

            case "ClipboardText":
                await DrainRemainingDataAsync(dataStream);
                PublishClipboardReceived(sender, packet.Content);
                ApplyIncomingClipboardText(sender, packet.Content);
                break;

            case "ClipboardSyncReq":
                await DrainRemainingDataAsync(dataStream);
                await HandleIncomingClipboardSyncRequestAsync(packet, sender);
                break;

            case "ClipboardSyncResp":
                await DrainRemainingDataAsync(dataStream);
                ResolvePendingBooleanRequest(_pendingClipboardSyncRequests, packet.RequestId, packet.Accepted);

                if (packet.Accepted)
                {
                    ActivateClipboardSync(sender, $"对方已同意，已与 {GetDeviceDisplayName(sender)} 开启双向同步");
                    PublishClipboardSyncAuthorized(sender, initiatedByPeer: false);
                }
                break;

            case "FileReq":
                await DrainRemainingDataAsync(dataStream);
                await HandleIncomingFileRequestAsync(packet, sender);
                break;

            case "FileResp":
                await DrainRemainingDataAsync(dataStream);
                ResolvePendingBooleanRequest(_pendingFileRequests, packet.RequestId, packet.Accepted);

                await SaveChatRecordAsync(
                    sender,
                    DeviceChatDirection.Incoming,
                    DeviceChatEntryType.TransferStatus,
                    content: packet.Accepted
                        ? "对方已同意文件传输请求。"
                        : "对方已拒绝文件传输请求。",
                    requestId: packet.RequestId,
                    status: packet.Accepted ? "accepted" : "rejected");
                break;
                
            case "FileTransfer":
                if (_pendingDownloads.TryRemove(packet.RequestId, out var savePath))
                {
                    await HandleIncomingFileTransferToPendingPathAsync(packet, dataStream, sender, savePath);
                    return;
                }
                goto default;

            case "Legacy":
            default:
                if (await TryHandleManualFileTransferAsync(packet, dataStream, sender))
                {
                    return;
                }

                await DispatchGenericStreamAsync(packet, dataStream, sender);
                break;
        }
    }

    private async Task HandleIncomingFileTransferToPendingPathAsync(
        PacketMetadata packet,
        Stream dataStream,
        DeviceModel sender,
        string savePath)
    {
        bool success = false;
        var senderName = GetDeviceDisplayName(sender);
        StartTransferToast(packet.RequestId, false, packet.FileName, packet.Size, senderName);
        var onProgress = CreateTransferProgressHandler(
            packet.RequestId,
            isSending: false,
            fileName: packet.FileName,
            totalBytes: packet.Size,
            remoteName: senderName);

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
            CompleteTransferToast(packet.RequestId, false, packet.FileName, packet.Size, senderName);
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
        ShowIncomingFileSavedToast(sender, savePath);
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
        if (!string.Equals(packet.Type, "FileTransfer", StringComparison.OrdinalIgnoreCase))
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
            await using var fs = new FileStream(manualSavePath, FileMode.Create, FileAccess.Write);
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
            ShowIncomingFileSavedToast(sender, manualSavePath);
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

    private async Task HandleQuicConnectionAsync(QuicConnection connection)
    {
        try
        {
            using var streamCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var stream = await connection.AcceptInboundStreamAsync(streamCts.Token);
            
            // Read Metadata
            var lenBuffer = new byte[4];
            await ReadExactAsync(stream, lenBuffer, TimeSpan.FromSeconds(5));
            var len = BitConverter.ToInt32(lenBuffer);
            var metaBuffer = new byte[len];
            await ReadExactAsync(stream, metaBuffer, TimeSpan.FromSeconds(5));
            var metaJson = System.Text.Encoding.UTF8.GetString(metaBuffer);
            
            System.Diagnostics.Debug.WriteLine($"[QUIC] Received Metadata: {metaJson}");

            // Try parse as PacketMetadata
            PacketMetadata? packet = null;
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            try 
            {
                packet = JsonSerializer.Deserialize<PacketMetadata>(metaJson, jsonOptions);
            }
            catch 
            {
                // Fallback for legacy format: {"Meta": "..."}
                try  
                {
                    var doc = JsonDocument.Parse(metaJson);
                    if (doc.RootElement.TryGetProperty("Meta", out var metaProp))
                    {
                        packet = new PacketMetadata { Type = "Legacy", Meta = metaProp.GetString() ?? "" };
                    }   
                }
                catch { }
            }

            if (packet == null) 
            {
                System.Diagnostics.Debug.WriteLine($"[QUIC] Failed to parse metadata: {metaJson}");
                return;
            }

            var sender = new DeviceModel
            {
                Address = NormalizeAddress(connection.RemoteEndPoint.Address),
                Port = connection.RemoteEndPoint.Port
            };
            if (packet.SenderPort > 0) sender.Port = packet.SenderPort;
            sender = ApplySenderIdentity(sender, packet);
            
            System.Diagnostics.Debug.WriteLine($"[QUIC] Dispatching packet type: {packet.Type} from {sender.Address}:{sender.Port}");
            await DispatchPacketAsync(packet, stream, sender);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QUIC] Handle connection error: {ex}");
        }
    }

    private static DeviceModel ApplySenderIdentity(DeviceModel sender, PacketMetadata packet)
    {
        if (!string.IsNullOrWhiteSpace(packet.SenderId))
        {
            sender.Id = packet.SenderId;
        }

        if (!string.IsNullOrWhiteSpace(packet.SenderName))
        {
            sender.Name = packet.SenderName.Trim();
        }

        if (string.IsNullOrWhiteSpace(sender.Name))
        {
            sender.Name = "\u672a\u77e5\u8bbe\u5907";
        }

        return sender;
    }

    private static IPEndPoint CreateTargetEndPoint(IPAddress address, int port)
    {
        var normalizedAddress = NormalizeAddress(address);

        if (normalizedAddress.AddressFamily == AddressFamily.InterNetworkV6 &&
            normalizedAddress.IsIPv6LinkLocal &&
            normalizedAddress.ScopeId == 0)
        {
            normalizedAddress = TryAttachLocalScopeId(normalizedAddress);
        }

        return new IPEndPoint(normalizedAddress, port);
    }

    private static IPAddress NormalizeAddress(IPAddress address)
    {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    private static bool ShouldReplaceDiscoveredAddress(IPAddress currentAddress, IPAddress candidateAddress)
    {
        if (currentAddress.Equals(candidateAddress))
        {
            return false;
        }

        var currentFamily = currentAddress.AddressFamily;
        var candidateFamily = candidateAddress.AddressFamily;

        if (currentFamily == AddressFamily.InterNetwork && candidateFamily == AddressFamily.InterNetworkV6)
        {
            return false;
        }

        if (currentFamily == AddressFamily.InterNetworkV6 && candidateFamily == AddressFamily.InterNetwork)
        {
            return true;
        }

        return true;
    }

    private static IPAddress TryAttachLocalScopeId(IPAddress address)
    {
        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                var ipProps = networkInterface.GetIPProperties();
                var ipv6Index = ipProps.GetIPv6Properties()?.Index;
                if (ipv6Index.HasValue && ipv6Index.Value > 0)
                {
                    return new IPAddress(address.GetAddressBytes(), ipv6Index.Value);
                }
            }
        }
        catch
        {
            // Keep original address if we cannot infer a scope id.
        }

        return address;
    }
    
    private void StartUdpDataListener(bool quicAvailable)
    {
        if (quicAvailable && _quicPort > 0)
        {
            // Listen on QuicPort + 1 (Fallback convention)
            _udpDataPort = _quicPort + 1;
            _advertisedPort = _quicPort;
            _udpDataClientV4 = new UdpClient(new IPEndPoint(IPAddress.Any, _udpDataPort));
        }
        else
        {
            // QUIC not available: pick a UDP port dynamically and advertise (port - 1)
            // so peers still use the same "base + 1" fallback convention.
            _udpDataClientV4 = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            _udpDataPort = ((IPEndPoint)_udpDataClientV4.Client.LocalEndPoint!).Port;
            _advertisedPort = _udpDataPort - 1;
        }

        _ = UdpListenLoop(_udpDataClientV4, _discoveryCts!.Token);

        try
        {
            _udpDataClientV6 = new UdpClient(AddressFamily.InterNetworkV6);
            _udpDataClientV6.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
            _udpDataClientV6.Client.Bind(new IPEndPoint(IPAddress.IPv6Any, _udpDataPort));
            _ = UdpListenLoop(_udpDataClientV6, _discoveryCts!.Token);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"UDP IPv6 listener disabled: {ex.Message}");
            _udpDataClientV6 = null;
        }
    }
    
    private async Task UdpListenLoop(UdpClient client, CancellationToken token)
    {
        // Periodic cleanup task for stale sessions
        _ = Task.Run(new Func<Task>(async () => 
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                var now = DateTime.UtcNow;
                var stale = _udpSessions.Where(kv => (now - kv.Value.LastActivity).TotalMinutes > 2).Select(kv => kv.Key).ToList();
                foreach (var id in stale)
                {
                    if (_udpSessions.TryRemove(id, out var session))
                    {
                         session.DataStream.Dispose();
                         
                         // Check if this was an interrupted transfer
                         if (!string.IsNullOrEmpty(session.MetadataJson))
                         {
                             try
                             {
                                 var meta = JsonSerializer.Deserialize<PacketMetadata>(session.MetadataJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                                 if (meta != null && !string.IsNullOrEmpty(meta.RequestId))
                                 {
                        NotifyTransferInterrupted(meta.RequestId, "传输超时", false);
                                 }
                             }
                             catch { }
                         }
                    }
                }
            }
        }), token);

        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await client.ReceiveAsync(token);
                if (result.Buffer.Length < 26) continue;
                
                var buf = result.Buffer;
                var sessionId = new Guid(buf.AsSpan(0, 16));
                var offset = BitConverter.ToInt64(buf, 16);
                var type = buf[24];
                var isEnd = buf[25] == 1;
                var payloadLen = buf.Length - 26;
                
                var session = _udpSessions.GetOrAdd(sessionId, id => new UdpReassemblySession 
                { 
                    SessionId = id, 
                    Sender = new DeviceModel
                    {
                        Address = NormalizeAddress(result.RemoteEndPoint.Address),
                        Port = result.RemoteEndPoint.Port
                    }
                });
                
                session.LastActivity = DateTime.UtcNow;
                
                if (type == 0) // Metadata
                {
                    if (payloadLen > 0)
                    {
                         var metaStr = System.Text.Encoding.UTF8.GetString(buf, 26, payloadLen);
                         session.MetadataJson = metaStr;
                    }
                }
                else if (type == 1) // Data
                {
                    if (payloadLen > 0)
                    {
                        lock (session)
                        {
                            if (offset < 100 * 1024 * 1024) // 100MB RAM limit guard
                            {
                                if (session.DataStream.Position != offset)
                                    session.DataStream.Seek(offset, SeekOrigin.Begin);
                                session.DataStream.Write(buf, 26, payloadLen);
                            }
                        }
                    }
                }
                
                if (isEnd)
                {
                     if (_udpSessions.TryRemove(sessionId, out var completedSession))
                     {
                         completedSession.DataStream.Position = 0;
                         
                         PacketMetadata? packet = null;
                         if (!string.IsNullOrEmpty(completedSession.MetadataJson))
                         {
                             var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                             try { packet = JsonSerializer.Deserialize<PacketMetadata>(completedSession.MetadataJson, jsonOptions); } catch {}
                         }
                         
                         if (packet != null)
                         {
                             if (packet.SenderPort > 0) completedSession.Sender.Port = packet.SenderPort;
                             completedSession.Sender = ApplySenderIdentity(completedSession.Sender, packet);
                             await DispatchPacketAsync(packet, completedSession.DataStream, completedSession.Sender);
                         }
                     }
                }
            }
            catch { break; }
        }
    }

    private async Task ReadExactAsync(Stream stream, byte[] buffer, TimeSpan timeout)
    {
        int offset = 0;
        using var cts = new CancellationTokenSource();
        while (offset < buffer.Length)
        {
            cts.CancelAfter(timeout);
            try 
            {
                int read = await stream.ReadAsync(buffer, offset, buffer.Length - offset, cts.Token);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
            catch (OperationCanceledException)
            {
                 throw new TimeoutException("读取数据超时。");
            }
        }
    }

    private async Task CleanupLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(DiscoveryCleanupInterval, token);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                var now = DateTime.UtcNow;
                var staleDevices = DiscoveredDevices.Where(d => now - d.LastSeen > DiscoveryStaleTimeout).ToList();
                foreach (var device in staleDevices)
                {
                    DiscoveredDevices.Remove(device);
                    if (!string.IsNullOrWhiteSpace(device.Id))
                    {
                        _discoveredDeviceQuicCapabilities.TryRemove(device.Id, out _);
                    }
                }
            });
        }
    }

    private async Task DiscoveryLoop(CancellationToken token)
    {
        _discoveryUdpClientV4 = new UdpClient(AddressFamily.InterNetwork);
        _discoveryUdpClientV4.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _discoveryUdpClientV4.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

        try
        {
            _discoveryUdpClientV6 = new UdpClient(AddressFamily.InterNetworkV6);
            _discoveryUdpClientV6.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _discoveryUdpClientV6.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
            _discoveryUdpClientV6.Client.Bind(new IPEndPoint(IPAddress.IPv6Any, DiscoveryPort));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Discovery IPv6 listener disabled: {ex.Message}");
            _discoveryUdpClientV6 = null;
        }

        var multicastIpV4 = IPAddress.Parse(MulticastAddressV4);
        var multicastIpV6 = IPAddress.Parse(MulticastAddressV6);
        try
        {
            // Try allow loopback for testing
            _discoveryUdpClientV4.MulticastLoopback = true;
            if (_discoveryUdpClientV6 != null)
            {
                _discoveryUdpClientV6.MulticastLoopback = true;
            }

            // Simple join (picks default interface)
            try 
            {
                _discoveryUdpClientV4.JoinMulticastGroup(multicastIpV4);
            }
            catch { }

            if (_discoveryUdpClientV6 != null)
            {
                try
                {
                    _discoveryUdpClientV6.JoinMulticastGroup(multicastIpV6);
                }
                catch { }
            }

            // Iterate interfaces to ensure we listen on all of them
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus == OperationalStatus.Up &&
                    networkInterface.SupportsMulticast &&
                    networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var props = networkInterface.GetIPProperties();
                    int? ipv6IfIndex = null;
                    try
                    {
                        ipv6IfIndex = props.GetIPv6Properties()?.Index;
                    }
                    catch { }

                    foreach (var unicast in props.UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            try
                            {
                                _discoveryUdpClientV4.JoinMulticastGroup(multicastIpV4, unicast.Address);
                            }
                            catch { }
                        }
                        else if (unicast.Address.AddressFamily == AddressFamily.InterNetworkV6 &&
                                 ipv6IfIndex.HasValue)
                        {
                            try
                            {
                                _discoveryUdpClientV6?.JoinMulticastGroup(ipv6IfIndex.Value, multicastIpV6);
                            }
                            catch { }
                        }
                    }
                }
            }
        }
        catch (Exception ex) 
        { 
             System.Diagnostics.Debug.WriteLine($"Multicast Setup Error: {ex}");
        }

        var receiveTasks = new List<Task>
        {
            DiscoveryReceiveLoop(_discoveryUdpClientV4!, token)
        };

        if (_discoveryUdpClientV6 != null)
        {
            receiveTasks.Add(DiscoveryReceiveLoop(_discoveryUdpClientV6, token));
        }

        await Task.WhenAll(receiveTasks);

        async Task DiscoveryReceiveLoop(UdpClient client, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await client.ReceiveAsync(ct);
                    var json = System.Text.Encoding.UTF8.GetString(result.Buffer);
                    var info = JsonSerializer.Deserialize<DiscoveryInfo>(json);

                    if (info != null && !string.IsNullOrEmpty(info.Id))
                    {
                        if (info.Id == _myId.ToString()) continue;
                        _discoveredDeviceQuicCapabilities[info.Id] = info.SupportsQuic;

                        _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            var endpointAddress = NormalizeAddress(result.RemoteEndPoint.Address);
                            var existing = DiscoveredDevices.FirstOrDefault(d => d.Id == info.Id);
                            if (existing == null)
                            {
                                var duplicateEndpoint = DiscoveredDevices.FirstOrDefault(d =>
                                    d.Address.Equals(endpointAddress) && d.Port == info.Port);

                                if (duplicateEndpoint != null)
                                {
                                    DiscoveredDevices.Remove(duplicateEndpoint);
                                    if (!string.IsNullOrWhiteSpace(duplicateEndpoint.Id))
                                    {
                                        _discoveredDeviceQuicCapabilities.TryRemove(duplicateEndpoint.Id, out _);
                                    }
                                }

                                var device = new DeviceModel
                                {
                                    Id = info.Id,
                                    Name = string.IsNullOrWhiteSpace(info.Name) ? "\u672a\u77e5\u8bbe\u5907" : info.Name.Trim(),
                                    Address = endpointAddress,
                                    Port = info.Port,
                                    LastSeen = DateTime.UtcNow
                                };
                                DiscoveredDevices.Add(device);
                            }
                            else
                            {
                                existing.LastSeen = DateTime.UtcNow;
                                existing.Name = string.IsNullOrWhiteSpace(info.Name) ? "\u672a\u77e5\u8bbe\u5907" : info.Name.Trim();
                                if (ShouldReplaceDiscoveredAddress(existing.Address, endpointAddress))
                                {
                                    existing.Address = endpointAddress;
                                }
                                existing.Port = info.Port;
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Discovery Receive Error: {ex}");
                }
            }
        }
    }

    private async Task BroadcastLoop(CancellationToken token)
    {
        var multicastIpV4 = IPAddress.Parse(MulticastAddressV4);
        var multicastIpV6 = IPAddress.Parse(MulticastAddressV6);
        var multicastEndpointV4 = new IPEndPoint(multicastIpV4, DiscoveryPort);
        
        while (!token.IsCancellationRequested)
        {
            try
            {
                var info = new DiscoveryInfo
                {
                    Id = _myId.ToString(),
                    Name = GetLocalDisplayName(),
                    Port = GetAdvertisedPort(),
                    SupportsQuic = _supportsQuicTransport
                };
                var json = JsonSerializer.Serialize(info);
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);

                // Broadcast on all suitable interfaces
                foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus == OperationalStatus.Up &&
                        networkInterface.SupportsMulticast &&
                        networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        var props = networkInterface.GetIPProperties();
                        foreach (var unicast in props.UnicastAddresses)
                        {
                            if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                try
                                {
                                     using var client = new UdpClient();
                                     // Bind to the specific interface address to ensure the packet goes out through it
                                     client.Client.Bind(new IPEndPoint(unicast.Address, 0));
                                     client.Ttl = DiscoveryIpv4Ttl;
                                     await client.SendAsync(bytes, bytes.Length, multicastEndpointV4);
                                 }
                                 catch { }
                             }
                             else if (unicast.Address.AddressFamily == AddressFamily.InterNetworkV6)
                             {
                                 try
                                 {
                                     using var client = new UdpClient(AddressFamily.InterNetworkV6);
                                     client.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
                                     client.Client.Bind(new IPEndPoint(unicast.Address, 0));

                                     var multicastAddressV6WithScope =
                                         new IPAddress(multicastIpV6.GetAddressBytes(), unicast.Address.ScopeId);
                                     var multicastEndpointV6 = new IPEndPoint(multicastAddressV6WithScope, DiscoveryPort);
                                     await client.SendAsync(bytes, bytes.Length, multicastEndpointV6);
                                 }
                                 catch { }
                             }
                         }
                     }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Broadcast Error: {ex}");
            }
            await Task.Delay(DiscoveryBroadcastInterval, token);
        }
    }

    private X509Certificate2 GenerateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=KitopiaDevice", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        
        // Key Usage: DigitalSignature is required for TLS 1.3
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, 
            false));
            
        // Enhanced Key Usage: Server Authentication (1.3.6.1.5.5.7.3.1) is required for QUIC/TLS servers
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, 
            false));

        var cert = request.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddYears(1));
        
        // Export/Import as PFX to ensure the private key is properly associated and accessible for SChannel/MsQuic on Windows
        return new X509Certificate2(cert.Export(X509ContentType.Pfx));
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

    private static IToastService? GetToastService()
    {
        try
        {
            return ServiceManager.Services?.GetService(typeof(IToastService)) as IToastService;
        }
        catch
        {
            return null;
        }
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

        var toastService = GetToastService();
        if (toastService is null)
        {
            return;
        }

        var (action, direction, title) = GetTransferToastText(isSending, completed: false);
        var detail = totalBytes > 0
            ? $"{FormatFileSize(0)} / {FormatFileSize(totalBytes)}"
            : "准备中...";
        var handle = toastService.ShowProgress(
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
    }

    private enum RequestDecision
    {
        Rejected,
        Accepted,
        TimedOut
    }

    private class IncomingFileRequestContext
    {
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DeviceModel Sender { get; set; } = new();
    }
    
    private class UdpReassemblySession
    {
        public Guid SessionId { get; set; }
        public DeviceModel Sender { get; set; } = new();
        public MemoryStream DataStream { get; } = new();
        public string MetadataJson { get; set; } = string.Empty;
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    }

    private class DiscoveryInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Port { get; set; }
        public bool SupportsQuic { get; set; } = true;
    }

    private class PacketMetadata
    {
        public string Type { get; set; } = "";
        public string Meta { get; set; } = "";
        public string Content { get; set; } = "";
        public long Size { get; set; }
        public string RequestId { get; set; } = "";
        public string FileName { get; set; } = "";
        public bool Accepted { get; set; }
        public int SenderPort { get; set; }
        public string SenderId { get; set; } = "";
        public string SenderName { get; set; } = "";
    }
}
