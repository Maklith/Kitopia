#region

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Platform;
using Avalonia.Threading;
using Core.Services;
using PluginCore;
using Serilog;
using Ursa.Controls;
using Vanara.PInvoke;

#endregion

namespace KitopiaAvalonia.Services;

public class ToastService : IToastService
{
    private static readonly TimeSpan CloseAnimationDuration = TimeSpan.FromMilliseconds(300);
    private static readonly ILogger Logger = LogManager.Logger.ForContext<ToastService>();
    private readonly ToastHostViewModel _hostViewModel = new();
    private readonly Dictionary<Guid, ToastItemViewModel> _items = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _autoCloseCtsMap = [];
    private readonly Dictionary<Guid, TaskCompletionSource<bool>> _dismissedTcsMap = [];
    private readonly List<SuppressedNotificationEntry> _suppressedEntries = [];
    private readonly SuppressedNotificationCenterViewModel _notificationCenterViewModel = new();
    private readonly DispatcherTimer _fullScreenMonitorTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly DispatcherTimer _trayBlinkTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private ToastShowWindow? _toastShowWindow;
    private SuppressedNotificationCenterWindow? _notificationCenterWindow;
    private bool _isUnregistered;
    private bool _isFlushingSuppressedRequests;
    private bool _trayBlinkPhaseVisible = true;
    private int _suppressedUnreadCount;
    private string? _latestSuppressedPreview;

    private const int MaxSuppressedQueueSize = 20;
    private const string TrayDefaultToolTip = "KitopiaAvalonia";
    private static readonly Uri TrayNormalIconUri = new("avares://KitopiaAvalonia/Assets/icon.png");
    private static readonly Uri TrayNotifyIconUri = new("avares://KitopiaAvalonia/Assets/icon_notify.png");

    public void Init()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_toastShowWindow is not null || _isUnregistered)
            {
                return;
            }

            _toastShowWindow = new ToastShowWindow
            {
                DataContext = _hostViewModel
            };
            _toastShowWindow.Show();
            _toastShowWindow.Hide();

            _fullScreenMonitorTimer.Tick += (_, _) => CheckSuppressedQueueOnUiThread();
            _trayBlinkTimer.Tick += (_, _) => BlinkTrayIconOnUiThread();
        }).Wait();
    }

    public Task Show(string header, string text, NotificationType notificationType = NotificationType.Information,
        Window? dialogWindow = null)
    {
        return Show(new ToastRequest
        {
            Header = header,
            Text = text,
            NotificationType = notificationType
        }, dialogWindow);
    }

    public Task Show(ToastRequest request, Window? dialogWindow = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        Logger.Debug(
            $"{nameof(ToastService)}的接口{nameof(Show)}被调用,header：{request.Header},text：{request.Text},type:{request.NotificationType}");
        if (_isUnregistered)
        {
            return Task.CompletedTask;
        }

        return dialogWindow is not null ? ShowDialog(request, dialogWindow) : ShowAndReturnCompletionTask(request);
    }

    private static async Task ShowDialog(ToastRequest request, Window dialogWindow)
    {
        var viewModel = new Controls.ToastDialogContentViewModel(request);
        var options = new OverlayDialogOptions
        {
            TopLevelHashCode = dialogWindow.GetHashCode(),
            CanLightDismiss = request.ShowCloseButton,
            HorizontalAnchor = HorizontalPosition.Center,
        };

        await OverlayDialog.ShowCustomModal<Controls.ToastDialogContent, Controls.ToastDialogContentViewModel, object>(
            viewModel,
            null,
            options);
        request.CloseAction?.Invoke();
    }

    public IToastProgressHandle ShowProgress(string header, string text,
        NotificationType notificationType = NotificationType.Information, double initialProgress = 0,
        bool isIndeterminate = false)
    {
        if (_isUnregistered)
        {
            return NoopToastProgressHandle.Instance;
        }

        var request = new ToastRequest
        {
            Header = header,
            Text = text,
            NotificationType = notificationType,
            AutoCloseDelay = null,
            ShowProgressBar = true,
            IsProgressIndeterminate = isIndeterminate,
            ProgressValue = isIndeterminate ? null : ClampProgress(initialProgress)
        };
        var toastId = ShowAndReturnId(request);
        return toastId == Guid.Empty ? NoopToastProgressHandle.Instance : new ToastProgressHandle(this, toastId);
    }

    public bool HasUnreadSuppressedNotifications()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return _suppressedUnreadCount > 0;
        }

        return Dispatcher.UIThread.InvokeAsync(() => _suppressedUnreadCount > 0).GetAwaiter().GetResult();
    }

    public bool TryOpenLatestSuppressedNotification()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return TryOpenLatestSuppressedNotificationOnUiThread();
        }

        return Dispatcher.UIThread.InvokeAsync(TryOpenLatestSuppressedNotificationOnUiThread).GetAwaiter().GetResult();
    }

    public void ClearUnreadSuppressedNotifications()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ClearUnreadSuppressedNotificationsOnUiThread();
            return;
        }

        Dispatcher.UIThread.Post(ClearUnreadSuppressedNotificationsOnUiThread);
    }

    public bool ShowSuppressedNotificationCenter()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return ShowSuppressedNotificationCenterOnUiThread();
        }

        return Dispatcher.UIThread.InvokeAsync(ShowSuppressedNotificationCenterOnUiThread).GetAwaiter().GetResult();
    }

    public void Unregister()
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                UnregisterOnUiThread();
            }
            else
            {
                Dispatcher.UIThread.InvokeAsync(UnregisterOnUiThread).Wait();
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "注销Toast服务失败");
        }
    }

    private void InvokeCloseAction(ToastRequest request)
    {
        request.CloseAction?.Invoke();
    }

    private async Task ShowAndReturnCompletionTask(ToastRequest request)
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                await ShowAndReturnCompletionTaskOnUiThread(request);
            }
            else
            {
                 await Dispatcher.UIThread.InvokeAsync(() => ShowAndReturnCompletionTaskOnUiThread(request));
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "显示Toast失败");
        }
        finally
        {
            try
            {
                InvokeCloseAction(request);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "执行Toast关闭回调失败");
            }
        }
    }

    private Task ShowAndReturnCompletionTaskOnUiThread(ToastRequest request)
    {
        if (_isUnregistered)
        {
            return Task.CompletedTask;
        }

        if (!_isFlushingSuppressedRequests && ShouldSuppressToastForFullScreenOnUiThread())
        {
            return SuppressToastOnUiThread(request);
        }

        var toastId = AddToastOnUiThread(request);
        if (toastId == Guid.Empty)
        {
            return Task.CompletedTask;
        }

        return GetOrCreateDismissedTaskOnUiThread(toastId);
    }

    private Guid ShowAndReturnId(ToastRequest request)
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                return ShowOnUiThread(request);
            }

            return Dispatcher.UIThread.InvokeAsync(() => ShowOnUiThread(request)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "显示Toast失败");
            return Guid.Empty;
        }
    }

    private Guid ShowOnUiThread(ToastRequest request)
    {
        if (_isUnregistered)
        {
            return Guid.Empty;
        }

        if (!_isFlushingSuppressedRequests && ShouldSuppressToastForFullScreenOnUiThread())
        {
            SuppressToastOnUiThread(request);
            return Guid.Empty;
        }

        return AddToastOnUiThread(request);
    }

    private Guid AddToastOnUiThread(ToastRequest request)
    {
        if (_isUnregistered)
        {
            return Guid.Empty;
        }

        EnsureWindowCreatedOnUiThread();
        var toastId = Guid.NewGuid();
        var toastItem = CreateToastItemOnUiThread(toastId, request);

        _items[toastId] = toastItem;
        _dismissedTcsMap[toastId] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _hostViewModel.Items.Add(toastItem);
        ScheduleAutoCloseOnUiThread(toastId, request.AutoCloseDelay);
        UpdateHostWindowVisibilityOnUiThread();
        return toastId;
    }

    private ToastItemViewModel CreateToastItemOnUiThread(Guid toastId, ToastRequest request)
    {
        Action? clickAction = request.ClickCallback is null
            ? null
            : () => ExecuteToastClick(toastId, request.ClickCallback, request.CloseOnClick);
        var item = new ToastItemViewModel(toastId, request, () => RemoveToast(toastId), clickAction);

        if (request.Actions is null)
        {
            return item;
        }

        foreach (var action in request.Actions)
        {
            var actionSnapshot = action;
            item.Actions.Add(new ToastActionViewModel(actionSnapshot.Text, actionSnapshot.IsPrimary,
                () => ExecuteAction(toastId, actionSnapshot)));
        }

        return item;
    }

    private Task SuppressToastOnUiThread(ToastRequest request)
    {
        var dismissedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _suppressedEntries.Add(new SuppressedNotificationEntry(
            request.Header,
            request.Text,
            DateTimeOffset.Now,
            request.ClickCallback,
            dismissedTcs));
        if (_suppressedEntries.Count > MaxSuppressedQueueSize)
        {
            var removed = _suppressedEntries[0];
            removed.DismissedTcs.TrySetResult(true);
            _suppressedEntries.RemoveAt(0);
        }
        _suppressedUnreadCount++;
        _latestSuppressedPreview = BuildPreviewText(request);
        EnsureSuppressionIndicatorsOnUiThread();
        return dismissedTcs.Task;
    }

    private bool ShowSuppressedNotificationCenterOnUiThread()
    {
        if (_suppressedEntries.Count == 0)
        {
            return false;
        }

        EnsureNotificationCenterWindowCreatedOnUiThread();
        if (_notificationCenterWindow!.IsVisible)
        {
            _notificationCenterWindow.Hide();
            return true;
        }

        RefreshNotificationCenterItemsOnUiThread();
        _notificationCenterWindow.RepositionNearCursor();
        _notificationCenterWindow.Show();
        _notificationCenterWindow.Activate();

        return true;
    }

    private void EnsureNotificationCenterWindowCreatedOnUiThread()
    {
        if (_notificationCenterWindow is not null)
        {
            return;
        }

        _notificationCenterWindow = new SuppressedNotificationCenterWindow
        {
            DataContext = _notificationCenterViewModel
        };
    }

    private void RefreshNotificationCenterItemsOnUiThread()
    {
        _notificationCenterViewModel.Items.Clear();
        for (var i = _suppressedEntries.Count - 1; i >= 0; i--)
        {
            var index = i;
            var entry = _suppressedEntries[index];
            _notificationCenterViewModel.Items.Add(new SuppressedNotificationItemViewModel(
                entry.Header,
                entry.Text,
                entry.CreatedAt,
                () => OpenSuppressedEntryOnUiThread(index)));
        }
    }

    private void OpenSuppressedEntryOnUiThread(int originalIndex)
    {
        if (originalIndex < 0 || originalIndex >= _suppressedEntries.Count)
        {
            return;
        }
        _notificationCenterWindow?.Hide();
        var entry = _suppressedEntries[originalIndex];
        try
        {
            entry.ClickCallback?.Invoke();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "打开被抑制通知失败");
        }

        _suppressedEntries.RemoveAt(originalIndex);
        entry.DismissedTcs.TrySetResult(true);
        _suppressedUnreadCount = Math.Max(0, _suppressedUnreadCount - 1);
        _latestSuppressedPreview = _suppressedEntries.Count > 0
            ? BuildPreviewText(new ToastRequest
            {
                Header = _suppressedEntries[^1].Header,
                Text = _suppressedEntries[^1].Text
            })
            : null;
        RefreshNotificationCenterItemsOnUiThread();
        if (_suppressedEntries.Count == 0)
        {
            ClearUnreadSuppressedNotificationsOnUiThread();
            
        }
        else
        {
            UpdateTrayToolTipOnUiThread();
        }
    }

    private bool TryOpenLatestSuppressedNotificationOnUiThread()
    {
        if (_suppressedEntries.Count == 0)
        {
            return false;
        }

        var latest = _suppressedEntries[^1];
        latest.ClickCallback?.Invoke();
        ClearUnreadSuppressedNotificationsOnUiThread();
        return latest.ClickCallback is not null;
    }

    private static string BuildPreviewText(ToastRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Header) && !string.IsNullOrWhiteSpace(request.Text))
        {
            return $"{request.Header}: {request.Text}";
        }

        return !string.IsNullOrWhiteSpace(request.Text) ? request.Text : request.Header;
    }

    private void EnsureSuppressionIndicatorsOnUiThread()
    {
        if (!_fullScreenMonitorTimer.IsEnabled)
        {
            _fullScreenMonitorTimer.Start();
        }

        if (!_trayBlinkTimer.IsEnabled)
        {
            _trayBlinkPhaseVisible = true;
            _trayBlinkTimer.Start();
        }

        UpdateTrayToolTipOnUiThread();
    }

    private void CheckSuppressedQueueOnUiThread()
    {
        if (_suppressedEntries.Count == 0)
        {
            StopSuppressionIndicatorsOnUiThread();
        }
    }

    private void BlinkTrayIconOnUiThread()
    {
        if (_suppressedEntries.Count == 0)
        {
            StopSuppressionIndicatorsOnUiThread();
            return;
        }

        var trayIcon = GetPrimaryTrayIconOnUiThread();
        if (trayIcon is null)
        {
            return;
        }

        _trayBlinkPhaseVisible = !_trayBlinkPhaseVisible;
        trayIcon.Icon = CreateTrayIcon(_trayBlinkPhaseVisible ? TrayNotifyIconUri : TrayNormalIconUri);
        UpdateTrayToolTipOnUiThread();
    }

    private void StopSuppressionIndicatorsOnUiThread()
    {
        if (_fullScreenMonitorTimer.IsEnabled)
        {
            _fullScreenMonitorTimer.Stop();
        }

        if (_trayBlinkTimer.IsEnabled)
        {
            _trayBlinkTimer.Stop();
        }

        var trayIcon = GetPrimaryTrayIconOnUiThread();
        if (trayIcon is null)
        {
            return;
        }

        trayIcon.IsVisible = true;
        trayIcon.Icon = CreateTrayIcon(TrayNormalIconUri);
        trayIcon.ToolTipText = TrayDefaultToolTip;
    }

    private void ClearSuppressionUnreadStateOnUiThread()
    {
        _suppressedUnreadCount = 0;
        _latestSuppressedPreview = null;
    }

    private void ClearUnreadSuppressedNotificationsOnUiThread()
    {
        foreach (var entry in _suppressedEntries)
        {
            entry.DismissedTcs.TrySetResult(true);
        }

        _suppressedEntries.Clear();
        ClearSuppressionUnreadStateOnUiThread();
        StopSuppressionIndicatorsOnUiThread();
    }

    private void UpdateTrayToolTipOnUiThread()
    {
        var trayIcon = GetPrimaryTrayIconOnUiThread();
        if (trayIcon is null)
        {
            return;
        }

        if (_suppressedUnreadCount <= 0)
        {
            trayIcon.ToolTipText = TrayDefaultToolTip;
            return;
        }

        var preview = _latestSuppressedPreview;
        if (!string.IsNullOrWhiteSpace(preview) && preview.Length > 32)
        {
            preview = preview[..32] + "...";
        }

        var blinkMark = _trayBlinkPhaseVisible ? "[新消息] " : "";
        trayIcon.ToolTipText = string.IsNullOrWhiteSpace(preview)
            ? $"{blinkMark}KitopiaAvalonia ({_suppressedUnreadCount})"
            : $"{blinkMark}KitopiaAvalonia ({_suppressedUnreadCount}) {preview}";
    }

    private static TrayIcon? GetPrimaryTrayIconOnUiThread()
    {
        var app = Application.Current;
        var icons = app is null ? null : TrayIcon.GetIcons(app);
        return icons is { Count: > 0 } ? icons[0] : null;
    }

    private static bool ShouldSuppressToastForFullScreenOnUiThread()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var foregroundHwnd = User32.GetForegroundWindow();
        if (foregroundHwnd.IsNull)
        {
            return false;
        }

        User32.GetWindowThreadProcessId(foregroundHwnd, out var foregroundProcessId);
        if (foregroundProcessId == Process.GetCurrentProcess().Id)
        {
            return false;
        }

        if (!User32.GetWindowRect(foregroundHwnd, out var windowRect))
        {
            return false;
        }

        if (windowRect.Width <= 0 || windowRect.Height <= 0)
        {
            return false;
        }

        var monitor = User32.MonitorFromWindow(foregroundHwnd, User32.MonitorFlags.MONITOR_DEFAULTTONULL);
        var monitorInfo = new User32.MONITORINFO();
        if (monitor.IsNull || !User32.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        var monitorRect = monitorInfo.rcMonitor;
        const int tolerance = 2;
        return Math.Abs(windowRect.left - monitorRect.left) <= tolerance
               && Math.Abs(windowRect.top - monitorRect.top) <= tolerance
               && Math.Abs(windowRect.right - monitorRect.right) <= tolerance
               && Math.Abs(windowRect.bottom - monitorRect.bottom) <= tolerance;
    }

    private Task GetOrCreateDismissedTaskOnUiThread(Guid toastId)
    {
        if (_dismissedTcsMap.TryGetValue(toastId, out var tcs))
        {
            return tcs.Task;
        }

        return Task.CompletedTask;
    }

    private void ExecuteAction(Guid toastId, ToastAction action)
    {
        try
        {
            action.Callback?.Invoke();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"执行Toast按钮动作失败, toastId: {toastId}");
        }

        if (action.ShouldCloseOnClick)
        {
            RemoveToast(toastId);
        }
    }

    private void ExecuteToastClick(Guid toastId, Action? clickCallback, bool closeOnClick)
    {
        try
        {
            clickCallback?.Invoke();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"执行Toast点击动作失败, toastId: {toastId}");
        }

        if (closeOnClick)
        {
            RemoveToast(toastId);
        }
    }

    private void EnsureWindowCreatedOnUiThread()
    {
        if (_toastShowWindow is not null)
        {
            return;
        }

        _toastShowWindow = new ToastShowWindow
        {
            DataContext = _hostViewModel
        };
        _toastShowWindow.Show();
        _toastShowWindow.Hide();
    }

    private void UpdateHostWindowVisibilityOnUiThread()
    {
        if (_toastShowWindow is null)
        {
            return;
        }

        if (_hostViewModel.Items.Count == 0)
        {
            if (_toastShowWindow.IsVisible)
            {
                _toastShowWindow.Hide();
            }

            return;
        }

        if (!_toastShowWindow.IsVisible)
        {
            _toastShowWindow.Show();
        }

        _toastShowWindow.Reposition();
        _toastShowWindow.ScrollToLatest();
    }

    private void RemoveToast(Guid toastId)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RemoveToastOnUiThread(toastId);
            return;
        }

        Dispatcher.UIThread.Post(() => RemoveToastOnUiThread(toastId));
    }

    private void RemoveToastOnUiThread(Guid toastId)
    {
        if (!TryBeginClosingToastOnUiThread(toastId))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(CloseAnimationDuration);
            await Dispatcher.UIThread.InvokeAsync(() => FinalizeRemoveToastOnUiThread(toastId));
        });
    }

    private void FinalizeRemoveToastOnUiThread(Guid toastId)
    {
        if (!_items.Remove(toastId, out var item))
        {
            return;
        }
        CancelAutoCloseOnUiThread(toastId);
        _hostViewModel.Items.Remove(item);
        CompleteDismissedTaskOnUiThread(toastId);
        UpdateHostWindowVisibilityOnUiThread();
    }

    private void CompleteDismissedTaskOnUiThread(Guid toastId)
    {
        if (!_dismissedTcsMap.Remove(toastId, out var dismissedTcs))
        {
            return;
        }

        dismissedTcs.TrySetResult(true);
    }

    private void ScheduleAutoCloseOnUiThread(Guid toastId, TimeSpan? delay)
    {
        CancelAutoCloseOnUiThread(toastId);

        if (!delay.HasValue || delay.Value <= TimeSpan.Zero)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _autoCloseCtsMap[toastId] = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay.Value, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (cts.IsCancellationRequested)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() => RemoveToastOnUiThread(toastId));
        });
    }

    private void CancelAutoCloseOnUiThread(Guid toastId)
    {
        if (!_autoCloseCtsMap.Remove(toastId, out var cts))
        {
            return;
        }

        cts.Cancel();
        cts.Dispose();
    }

    private void UpdateProgressToast(Guid toastId, double? progress, string? text, string? header,
        bool? isIndeterminate)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdateProgressToastOnUiThread(toastId, progress, text, header, isIndeterminate);
            return;
        }

        Dispatcher.UIThread.Post(() => UpdateProgressToastOnUiThread(toastId, progress, text, header, isIndeterminate));
    }

    private void UpdateProgressToastOnUiThread(Guid toastId, double? progress, string? text, string? header,
        bool? isIndeterminate)
    {
        if (!TryGetActiveToastItemOnUiThread(toastId, out var item))
        {
            return;
        }

        CancelAutoCloseOnUiThread(toastId);
        item.ShowProgressBar = true;
        ApplyHeaderAndText(item, header, text);

        if (isIndeterminate.HasValue)
        {
            item.IsProgressIndeterminate = isIndeterminate.Value;
        }

        if (progress.HasValue)
        {
            item.ProgressValue = ClampProgress(progress.Value);
            if (!isIndeterminate.GetValueOrDefault())
            {
                item.IsProgressIndeterminate = false;
            }
        }
        else if (isIndeterminate is true)
        {
            item.ProgressValue = null;
        }
    }

    private void CompleteProgressToast(Guid toastId, string? text, string? header, TimeSpan? autoCloseDelay)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            CompleteProgressToastOnUiThread(toastId, text, header, autoCloseDelay);
            return;
        }

        Dispatcher.UIThread.Post(() => CompleteProgressToastOnUiThread(toastId, text, header, autoCloseDelay));
    }

    private void CompleteProgressToastOnUiThread(Guid toastId, string? text, string? header, TimeSpan? autoCloseDelay)
    {
        if (!TryGetActiveToastItemOnUiThread(toastId, out var item))
        {
            return;
        }

        item.NotificationType = NotificationType.Success;
        item.ShowProgressBar = true;
        item.IsProgressIndeterminate = false;
        item.ProgressValue = 100;
        ApplyHeaderAndText(item, header, text);

        ScheduleAutoCloseOnUiThread(toastId, autoCloseDelay ?? TimeSpan.FromSeconds(2));
    }

    private void FailProgressToast(Guid toastId, string? text, string? header, TimeSpan? autoCloseDelay)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            FailProgressToastOnUiThread(toastId, text, header, autoCloseDelay);
            return;
        }

        Dispatcher.UIThread.Post(() => FailProgressToastOnUiThread(toastId, text, header, autoCloseDelay));
    }

    private void FailProgressToastOnUiThread(Guid toastId, string? text, string? header, TimeSpan? autoCloseDelay)
    {
        if (!TryGetActiveToastItemOnUiThread(toastId, out var item))
        {
            return;
        }

        item.NotificationType = NotificationType.Error;
        item.IsProgressIndeterminate = false;
        ApplyHeaderAndText(item, header, text);

        ScheduleAutoCloseOnUiThread(toastId, autoCloseDelay ?? TimeSpan.FromSeconds(5));
    }

    private bool TryBeginClosingToastOnUiThread(Guid toastId)
    {
        if (!_items.TryGetValue(toastId, out var item))
        {
            return false;
        }

        CancelAutoCloseOnUiThread(toastId);

        if (item.IsClosing)
        {
            return false;
        }

        item.IsClosing = true;
        return true;
    }

    private bool TryGetActiveToastItemOnUiThread(Guid toastId, out ToastItemViewModel item)
    {
        if (!_items.TryGetValue(toastId, out item!))
        {
            return false;
        }

        return !item.IsClosing;
    }

    private static void ApplyHeaderAndText(ToastItemViewModel item, string? header, string? text)
    {
        if (!string.IsNullOrWhiteSpace(header))
        {
            item.Header = header;
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            item.Text = text;
        }
    }

    private void UnregisterOnUiThread()
    {
        if (_isUnregistered)
        {
            return;
        }

        _isUnregistered = true;
        _fullScreenMonitorTimer.Stop();
        _trayBlinkTimer.Stop();
        foreach (var entry in _suppressedEntries)
        {
            entry.DismissedTcs.TrySetResult(true);
        }

        _suppressedEntries.Clear();
        ClearSuppressionUnreadStateOnUiThread();

        var trayIcon = GetPrimaryTrayIconOnUiThread();
        if (trayIcon is not null)
        {
            trayIcon.IsVisible = true;
            trayIcon.Icon = CreateTrayIcon(TrayNormalIconUri);
            trayIcon.ToolTipText = TrayDefaultToolTip;
        }

        var keys = _autoCloseCtsMap.Keys.ToArray();
        foreach (var key in keys)
        {
            CancelAutoCloseOnUiThread(key);
        }
        
        _items.Clear();
        _hostViewModel.Items.Clear();
        foreach (var dismissedTcs in _dismissedTcsMap.Values)
        {
            dismissedTcs.TrySetResult(true);
        }

        _dismissedTcsMap.Clear();
        if (_toastShowWindow is null)
        {
            return;
        }

        if (_toastShowWindow.IsVisible)
        {
            _toastShowWindow.Hide();
        }

        _toastShowWindow.Close();
        _toastShowWindow = null;

        _notificationCenterWindow?.ClosePermanently();
        _notificationCenterWindow = null;
    }

    private sealed record SuppressedNotificationEntry(
        string Header,
        string Text,
        DateTimeOffset CreatedAt,
        Action? ClickCallback,
        TaskCompletionSource<bool> DismissedTcs);

    private static WindowIcon CreateTrayIcon(Uri assetUri)
    {
        using var stream = AssetLoader.Open(assetUri);
        return new WindowIcon(stream);
    }

    private static double ClampProgress(double progress)
    {
        return System.Math.Max(0, System.Math.Min(100, progress));
    }
    
    private sealed class ToastProgressHandle(ToastService service, Guid toastId) : IToastProgressHandle
    {
        private int _isClosed;

        public void Update(double? progress = null, string? text = null, string? header = null,
            bool? isIndeterminate = null)
        {
            if (Volatile.Read(ref _isClosed) == 1)
            {
                return;
            }

            service.UpdateProgressToast(toastId, progress, text, header, isIndeterminate);
        }

        public void Complete(string? text = null, string? header = null, TimeSpan? autoCloseDelay = null)
        {
            if (Volatile.Read(ref _isClosed) == 1)
            {
                return;
            }

            service.CompleteProgressToast(toastId, text, header, autoCloseDelay);
        }

        public void Fail(string? text = null, string? header = null, TimeSpan? autoCloseDelay = null)
        {
            if (Volatile.Read(ref _isClosed) == 1)
            {
                return;
            }

            service.FailProgressToast(toastId, text, header, autoCloseDelay);
        }

        public void Close()
        {
            if (Interlocked.Exchange(ref _isClosed, 1) == 1)
            {
                return;
            }

            service.RemoveToast(toastId);
        }
    }

    private sealed class NoopToastProgressHandle : IToastProgressHandle
    {
        public static IToastProgressHandle Instance { get; } = new NoopToastProgressHandle();

        public void Update(double? progress = null, string? text = null, string? header = null,
            bool? isIndeterminate = null)
        {
        }

        public void Complete(string? text = null, string? header = null, TimeSpan? autoCloseDelay = null)
        {
        }

        public void Fail(string? text = null, string? header = null, TimeSpan? autoCloseDelay = null)
        {
        }

        public void Close()
        {
        }
    }
}
