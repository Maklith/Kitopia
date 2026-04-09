using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Services.DeviceCommunication.Discovery;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Requests;
using Core.Services.DeviceCommunication.Transport;
using PluginCore;
using Serilog;

namespace Core.Services.DeviceCommunication.Clipboard;

public sealed class ClipboardSyncService : IClipboardSyncService
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<ClipboardSyncService>();
    private const int ClipboardPollIntervalMs = 800;
    private static readonly TimeSpan ClipboardEchoSuppressionWindow = TimeSpan.FromSeconds(2);

    private readonly IClipboardService _clipboardService;
    private readonly IRequestTracker _requestTracker;
    private readonly ITransportService _transportService;
    private readonly IDeviceDiscoveryService _discoveryService;
    private readonly Func<int> _getAdvertisedPort;
    private readonly Func<Guid> _getLocalDeviceId;
    private readonly Func<string> _getLocalDeviceName;
    private readonly object _syncLock = new();

    private CancellationTokenSource? _syncCts;
    private DeviceModel? _targetDevice;
    private string _lastSyncedClipboardText = string.Empty;
    private string _lastInboundClipboardText = string.Empty;
    private DateTime _lastInboundClipboardUtc = DateTime.MinValue;
    private int _isApplyingRemoteClipboard;
    private bool _isEnabled;

    public ClipboardSyncService(
        IClipboardService clipboardService,
        IRequestTracker requestTracker,
        ITransportService transportService,
        IDeviceDiscoveryService discoveryService,
        Func<int> getAdvertisedPort,
        Func<Guid> getLocalDeviceId,
        Func<string> getLocalDeviceName)
    {
        _clipboardService = clipboardService;
        _requestTracker = requestTracker;
        _transportService = transportService;
        _discoveryService = discoveryService;
        _getAdvertisedPort = getAdvertisedPort;
        _getLocalDeviceId = getLocalDeviceId;
        _getLocalDeviceName = getLocalDeviceName;
        _lastSyncedClipboardText = _clipboardService.GetText() ?? string.Empty;
    }

    public bool IsEnabled
    {
        get
        {
            lock (_syncLock)
            {
                return _isEnabled;
            }
        }
    }

    public DeviceModel? TargetDevice
    {
        get
        {
            lock (_syncLock)
            {
                return _targetDevice is null ? null : CloneDeviceModel(_targetDevice);
            }
        }
    }

    public event EventHandler<DeviceClipboardSyncStateChangedEventArgs>? StateChanged;
    public event EventHandler<DeviceClipboardSyncAuthorizedEventArgs>? Authorized;

    public async Task SendTextAsync(DeviceModel target, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var metadata = CreatePacketMetadata(PacketTypes.ClipboardText, content: text);
        await _transportService.SendAsync(target, metadata, Stream.Null);
        Logger.Information(
            "[ClipboardSync] Sent to {Target} ({TargetId}): {ClipboardPreview}",
            GetDeviceDisplayName(target),
            target.Id,
            ToClipboardLogPreview(text));
    }

    public async Task<bool> RequestAsync(DeviceModel target, TimeSpan timeout, string duplicateError)
    {
        var requestMetadata = CreatePacketMetadata(
            PacketTypes.ClipboardSyncRequest,
            requestId: Guid.NewGuid().ToString("N"));

        var decision = await _requestTracker.WaitForBooleanResponseAsync(
            requestMetadata.RequestId,
            () => _transportService.SendAsync(target, requestMetadata, Stream.Null),
            timeout,
            duplicateError);
        return decision == RequestDecision.Accepted;
    }

    public async Task<bool> EnableAsync(DeviceModel target, TimeSpan timeout, string duplicateError)
    {
        var resolvedTarget = ResolveDiscoveredDevice(target) ?? target;
        UpdateState(
            isEnabled: false,
            target: resolvedTarget,
            status: $"等待 {GetDeviceDisplayName(resolvedTarget)} 同意同步请求...",
            keepTargetWhenDisabled: true);

        var accepted = await RequestAsync(resolvedTarget, timeout, duplicateError);
        if (!accepted)
        {
            UpdateState(
                isEnabled: false,
                target: resolvedTarget,
                status: "对方未同意同步请求或请求超时",
                keepTargetWhenDisabled: true);
            return false;
        }

        if (!IsEnabled || TargetDevice is not { } current || !IsSameDevice(current, resolvedTarget))
        {
            Activate(resolvedTarget, $"已与 {GetDeviceDisplayName(resolvedTarget)} 建立双向剪贴板同步");
        }

        return true;
    }

    public void Disable(string status = "实时同步剪贴板已关闭", bool keepTargetWhenDisabled = false)
    {
        Deactivate(status, keepTargetWhenDisabled);
    }

    public void ApplyIncomingClipboardText(DeviceModel sender, string text)
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
        lock (_syncLock)
        {
            enabled = _isEnabled;
            target = _targetDevice is null ? null : CloneDeviceModel(_targetDevice);
        }

        if (!enabled || target is null || !IsSameDevice(target, sender))
        {
            return;
        }

        lock (_syncLock)
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
                UpdateState(true, target, "接收远端剪贴板失败", keepTargetWhenDisabled: true);
                return;
            }

            lock (_syncLock)
            {
                _lastSyncedClipboardText = text;
                _lastInboundClipboardText = text;
                _lastInboundClipboardUtc = DateTime.UtcNow;
            }

            UpdateState(
                isEnabled: true,
                target: target,
                status: $"已从 {GetDeviceDisplayName(sender)} 同步剪贴板",
                keepTargetWhenDisabled: true);
        }
        catch
        {
            UpdateState(true, target, "接收远端剪贴板失败", keepTargetWhenDisabled: true);
        }
        finally
        {
            Interlocked.Exchange(ref _isApplyingRemoteClipboard, 0);
        }
    }

    public async Task HandleIncomingRequestAsync(
        PacketMetadata packet,
        DeviceModel sender,
        Func<DeviceModel, Task<bool>> promptIncomingAsync)
    {
        if (string.IsNullOrWhiteSpace(packet.RequestId))
        {
            return;
        }

        var accepted = await promptIncomingAsync(sender);
        var responseMeta = CreatePacketMetadata(
            PacketTypes.ClipboardSyncResponse,
            requestId: packet.RequestId,
            accepted: accepted);
        await _transportService.SendAsync(sender, responseMeta, Stream.Null);

        if (!accepted)
        {
            return;
        }

        Activate(sender, $"已同意 {GetDeviceDisplayName(sender)} 的请求，双向同步已开启");
        Authorized?.Invoke(this, new DeviceClipboardSyncAuthorizedEventArgs(CloneDeviceModel(sender), true));
    }

    public Task HandleIncomingResponseAsync(PacketMetadata packet, DeviceModel sender)
    {
        _requestTracker.Resolve(packet.RequestId, packet.Accepted);
        if (!packet.Accepted)
        {
            return Task.CompletedTask;
        }

        Activate(sender, $"对方已同意，已与 {GetDeviceDisplayName(sender)} 开启双向同步");
        Authorized?.Invoke(this, new DeviceClipboardSyncAuthorizedEventArgs(CloneDeviceModel(sender), false));
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Disable();
    }

    private void UpdateState(bool isEnabled, DeviceModel? target, string status, bool keepTargetWhenDisabled)
    {
        DeviceModel? targetSnapshot;
        lock (_syncLock)
        {
            _isEnabled = isEnabled;
            if (target is not null)
            {
                _targetDevice = CloneDeviceModel(target);
            }
            else if (!keepTargetWhenDisabled)
            {
                _targetDevice = null;
            }

            targetSnapshot = _targetDevice is null ? null : CloneDeviceModel(_targetDevice);
        }

        StateChanged?.Invoke(this, new DeviceClipboardSyncStateChangedEventArgs(isEnabled, targetSnapshot, status));
    }

    private void Activate(DeviceModel target, string status)
    {
        var resolvedTarget = ResolveDiscoveredDevice(target) ?? target;
        var initialClipboardText = _clipboardService.GetText() ?? string.Empty;
        var shouldStartMonitor = false;
        CancellationToken monitorToken = default;

        lock (_syncLock)
        {
            _isEnabled = true;
            _targetDevice = CloneDeviceModel(resolvedTarget);
            _lastSyncedClipboardText = initialClipboardText;

            if (_syncCts is null)
            {
                _syncCts = new CancellationTokenSource();
                shouldStartMonitor = true;
                monitorToken = _syncCts.Token;
            }
        }

        if (shouldStartMonitor)
        {
            _ = MonitorClipboardLoopAsync(monitorToken);
        }

        UpdateState(true, resolvedTarget, status, keepTargetWhenDisabled: true);
    }

    private void Deactivate(string status, bool keepTargetWhenDisabled)
    {
        CancellationTokenSource? ctsToDispose;
        lock (_syncLock)
        {
            _isEnabled = false;
            ctsToDispose = _syncCts;
            _syncCts = null;
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

        UpdateState(false, null, status, keepTargetWhenDisabled);
    }

    private async Task MonitorClipboardLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                DeviceModel? target;
                bool enabled;
                lock (_syncLock)
                {
                    enabled = _isEnabled;
                    target = _targetDevice is null ? null : CloneDeviceModel(_targetDevice);
                }

                if (enabled && target is not null)
                {
                    var discoveredTarget = ResolveDiscoveredDevice(target);
                    if (discoveredTarget is null)
                    {
                        Deactivate("同步目标设备已离线，请重新选择", keepTargetWhenDisabled: false);
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
                            lock (_syncLock)
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
                                await SendTextAsync(discoveredTarget, currentText);
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
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
            SenderPort = _getAdvertisedPort(),
            SenderId = _getLocalDeviceId().ToString(),
            SenderName = _getLocalDeviceName()
        };
    }

    private DeviceModel? ResolveDiscoveredDevice(DeviceModel candidate)
    {
        var devices = _discoveryService.Devices;
        if (!string.IsNullOrWhiteSpace(candidate.Id))
        {
            var matchedById = devices.FirstOrDefault(device =>
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

        return devices.FirstOrDefault(device =>
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

    private static string ToClipboardLogPreview(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "<empty>";
        }

        var normalized = text.Replace("\r", "\\r").Replace("\n", "\\n");
        const int maxLen = 120;
        return normalized.Length <= maxLen ? normalized : normalized[..maxLen] + "...";
    }
}
