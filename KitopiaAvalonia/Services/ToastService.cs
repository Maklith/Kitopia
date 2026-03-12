#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using Core.Services;
using PluginCore;
using Serilog;

#endregion

namespace KitopiaAvalonia.Services;

public class ToastService : IToastService
{
    private static readonly TimeSpan CloseAnimationDuration = TimeSpan.FromMilliseconds(300);
    private static readonly ILogger Logger = LogManager.Logger.ForContext<ToastService>();
    private readonly ToastHostViewModel _hostViewModel = new();
    private readonly Dictionary<Guid, ToastItemViewModel> _items = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _autoCloseCtsMap = [];
    private ToastShowWindow? _toastShowWindow;
    private bool _isUnregistered;

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
        }).Wait();
    }

    public void Show(string header, string text, NotificationType notificationType = NotificationType.Information)
    {
        Show(new ToastRequest
        {
            Header = header,
            Text = text,
            NotificationType = notificationType
        });
    }

    public void Show(ToastRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_isUnregistered)
        {
            return;
        }

        Logger.Debug(
            $"{nameof(ToastService)}的接口{nameof(Show)}被调用,header：{request.Header},text：{request.Text},type:{request.NotificationType}");
        _ = ShowAndReturnId(request);
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

        EnsureWindowCreatedOnUiThread();
        var toastId = Guid.NewGuid();
        var toastItem = new ToastItemViewModel(toastId, request.Header, request.Text, request.NotificationType,
            request.ShowCloseButton, request.ShowProgressBar, request.IsProgressIndeterminate, request.ProgressValue,
            () => RemoveToast(toastId));

        if (request.Actions is not null)
        {
            foreach (var action in request.Actions)
            {
                var actionSnapshot = action;
                toastItem.Actions.Add(new ToastActionViewModel(actionSnapshot.Text, actionSnapshot.IsPrimary,
                    () => ExecuteAction(toastId, actionSnapshot)));
            }
        }

        _items[toastId] = toastItem;
        _hostViewModel.Items.Add(toastItem);
        ScheduleAutoCloseOnUiThread(toastId, request.AutoCloseDelay);
        UpdateHostWindowVisibilityOnUiThread();
        return toastId;
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

        if (action.CloseOnClick)
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
        if (!_items.TryGetValue(toastId, out var item))
        {
            return;
        }

        CancelAutoCloseOnUiThread(toastId);

        if (item.IsClosing)
        {
            return;
        }

        item.IsClosing = true;

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
        UpdateHostWindowVisibilityOnUiThread();
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
        if (!_items.TryGetValue(toastId, out var item))
        {
            return;
        }

        if (item.IsClosing)
        {
            return;
        }

        CancelAutoCloseOnUiThread(toastId);
        item.ShowProgressBar = true;

        if (!string.IsNullOrWhiteSpace(header))
        {
            item.Header = header;
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            item.Text = text;
        }

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
        if (!_items.TryGetValue(toastId, out var item))
        {
            return;
        }

        if (item.IsClosing)
        {
            return;
        }

        item.NotificationType = NotificationType.Success;
        item.ShowProgressBar = true;
        item.IsProgressIndeterminate = false;
        item.ProgressValue = 100;

        if (!string.IsNullOrWhiteSpace(header))
        {
            item.Header = header;
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            item.Text = text;
        }

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
        if (!_items.TryGetValue(toastId, out var item))
        {
            return;
        }

        if (item.IsClosing)
        {
            return;
        }

        item.NotificationType = NotificationType.Error;
        item.IsProgressIndeterminate = false;

        if (!string.IsNullOrWhiteSpace(header))
        {
            item.Header = header;
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            item.Text = text;
        }

        ScheduleAutoCloseOnUiThread(toastId, autoCloseDelay ?? TimeSpan.FromSeconds(5));
    }

    private void UnregisterOnUiThread()
    {
        if (_isUnregistered)
        {
            return;
        }

        _isUnregistered = true;
        var keys = _autoCloseCtsMap.Keys.ToArray();
        foreach (var key in keys)
        {
            CancelAutoCloseOnUiThread(key);
        }

        _items.Clear();
        _hostViewModel.Items.Clear();
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
    }

    private static double ClampProgress(double progress)
    {
        return Math.Max(0, Math.Min(100, progress));
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
