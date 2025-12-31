#region

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using Core.Services;
using PluginCore;
using Serilog;
using Ursa.Controls;
using Vanara.PInvoke;
using Notification = Ursa.Controls.Notification;
using WindowNotificationManager = Ursa.Controls.WindowNotificationManager;

#endregion

namespace Core.Window;

public class ToastService : IToastService
{
    private static ILogger Log = LogManager.Logger.ForContext<ToastService>();
    private ToastShowWindow _toastShowWindow;

    public void Init()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _toastShowWindow = new ToastShowWindow();
            _toastShowWindow.Show();
            _toastShowWindow.IsVisible = false;
        });
    }
    int counter = 0;
    public void Show(string header, string text, NotificationType notificationType = NotificationType.Information)
    {
        Log.Debug($"{nameof(ToastService)}的接口{nameof(Show)}被调用,header：{header},text：{text}");
        var foregroundWindow = User32.GetForegroundWindow();
        if (foregroundWindow.IsNull || foregroundWindow.IsInvalid)
        {
            Log.Warning("无法获取前台窗口，Toast显示失败");
            return;
        }

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var windowFromIntPtr = GetWindowFromIntPtr(foregroundWindow.DangerousGetHandle());

            if (windowFromIntPtr == null||windowFromIntPtr.TryGetPlatformHandle()!.Handle==_toastShowWindow.TryGetPlatformHandle()?.Handle)
            {
                Log.Warning("无法通过前台窗口句柄获取Avalonia窗口，使用全局 Toast显示");
                _toastShowWindow.IsVisible = true;
                windowFromIntPtr = _toastShowWindow;
                var windowToastManager =
                    WindowNotificationManager.TryGetNotificationManager(windowFromIntPtr, out var manager)
                        ? manager
                        : new WindowNotificationManager(windowFromIntPtr);
                windowToastManager.Position = NotificationPosition.BottomRight;
                Interlocked.Add(ref  counter, 1);
                windowToastManager!.Show(
                    new Notification($"Kitopia{header}", text),
                    showIcon: true,
                    showClose: true,
                    onClose: () =>
                    {
                        Interlocked.Add(ref counter, -1);
                        if (counter == 0)
                            _toastShowWindow.IsVisible = false;
                    },
                    type: notificationType);
            }
            else
            {
                // var windowToastManager = WindowToastManager.TryGetToastManager(windowFromIntPtr, out var manager)
                //     ? manager
                //     : new WindowToastManager(windowFromIntPtr);
                // windowToastManager!.Show(
                //     new Toast($"{header} {text}"),
                //     showIcon: true,
                //     showClose: true,
                //     type: notificationType);
                var windowToastManager =
                    WindowNotificationManager.TryGetNotificationManager(windowFromIntPtr, out var manager)
                        ? manager
                        : new WindowNotificationManager(windowFromIntPtr);
                windowToastManager.Position = NotificationPosition.BottomRight;
                windowToastManager!.Show(
                    new Notification($"Kitopia{header}", text),
                    showIcon: true,
                    showClose: true,
                    type: notificationType);
            }
        });
    }

    public void Unregister()
    {
    }

    public static Avalonia.Controls.Window? GetWindowFromIntPtr(IntPtr hwnd)
    {
        // 遍历应用程序所有打开的窗口
        if (Application.Current.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return null;
        }

        foreach (var window in desktop.Windows)
        {
            // 获取窗口平台 handle
            if (window.TryGetPlatformHandle()?.Handle == hwnd)
            {
                return window;
            }
        }

        return null;
    }
}