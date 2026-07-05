using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Layout;
using Avalonia.Threading;
using PluginCore;

namespace Kitopia.Mobile.Services;

public sealed class MobileToastService : IToastService
{
    private readonly MobileTopLevelContext _topLevel;

    public MobileToastService(MobileTopLevelContext topLevel)
    {
        _topLevel = topLevel;
    }

    public void Init() { }

    public Task Show(string header, string text, NotificationType notificationType = NotificationType.Information,
        Window? dialogWindow = null)
    {
        return Show(new ToastRequest { Header = header, Text = text, NotificationType = notificationType }, dialogWindow);
    }

    public async Task Show(ToastRequest request, Window? dialogWindow = null)
    {
        if (dialogWindow is not null)
        {
            await ShowAsDialog(request, dialogWindow);
            return;
        }

        var notifier = NativeNotifier.Current;
        if (notifier is not null)
        {
            notifier.Show(request.Header, request.Text);
            return;
        }

        var manager = GetDesktopToastManager();
        if (manager is null) return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            manager.Show(new Notification(
                request.Header,
                request.Text,
                MapType(request.NotificationType),
                expiration: request.AutoCloseDelay ?? TimeSpan.FromSeconds(5),
                onClick: () => Dispatcher.UIThread.Post(() => request.ClickCallback?.Invoke())));
        });
    }

    public IToastProgressHandle ShowProgress(string header, string text,
        NotificationType notificationType = NotificationType.Information, double initialProgress = 0,
        bool isIndeterminate = false)
    {
        Show(header, text, notificationType);
        return new MobileProgressHandle(this, header);
    }

    public bool HasUnreadSuppressedNotifications() => false;
    public bool TryOpenLatestSuppressedNotification() => false;
    public bool ShowSuppressedNotificationCenter() => false;
    public void ClearUnreadSuppressedNotifications() { }
    public void Unregister() { }

    private Avalonia.Controls.Notifications.WindowNotificationManager? GetDesktopToastManager()
    {
        var tl = _topLevel.CurrentTopLevel;
        if (tl is null) return null;
        return new Avalonia.Controls.Notifications.WindowNotificationManager(tl)
        {
            Position = NotificationPosition.BottomCenter,
            MaxItems = 3
        };
    }

    private static NotificationType MapType(NotificationType type) => type;

    private static async Task ShowAsDialog(ToastRequest request, Window dialogWindow)
    {
        var textBox = new TextBox { Text = request.Text, IsReadOnly = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var okButton = new Button { Content = "确定" };

        var dialog = new Window
        {
            Title = request.Header,
            Width = 400,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = request.Header, FontSize = 16, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                    textBox,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { okButton }
                    }
                }
            }
        };

        var tcs = new TaskCompletionSource<bool>();
        okButton.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
        dialog.Closed += (_, _) => tcs.TrySetResult(false);

        await dialog.ShowDialog(dialogWindow);
        request.CloseAction?.Invoke();
    }

    private sealed class MobileProgressHandle : IToastProgressHandle
    {
        private readonly MobileToastService _service;
        private readonly string _header;
        private int _isClosed;

        public MobileProgressHandle(MobileToastService service, string header)
        {
            _service = service;
            _header = header;
        }

        public void Update(double? progress = null, string? text = null, string? header = null,
            bool? isIndeterminate = null) { }

        public void Complete(string? text = null, string? header = null, TimeSpan? autoCloseDelay = null)
        {
            if (System.Threading.Interlocked.Exchange(ref _isClosed, 1) == 1) return;
            _ = _service.Show(header ?? _header, text ?? "完成", NotificationType.Success);
        }

        public void Fail(string? text = null, string? header = null, TimeSpan? autoCloseDelay = null)
        {
            if (System.Threading.Interlocked.Exchange(ref _isClosed, 1) == 1) return;
            _ = _service.Show(header ?? _header, text ?? "失败", NotificationType.Error);
        }

        public void Close()
        {
            System.Threading.Interlocked.Exchange(ref _isClosed, 1);
        }
    }
}
