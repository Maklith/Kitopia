#region

using Windows.UI.Notifications;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using Core.Services;
using PluginCore;
using Serilog;
using Ursa.Controls;
using Vanara.PInvoke;
using WinRT;

#endregion

namespace Core.Window;

public class ToastService : IToastService
{
    private static ILogger Log = LogManager.Logger.ForContext<ToastService>();
    private ToastNotifier _toastNotifier;

    public void Init()
    {
        ComWrappersSupport.InitializeComWrappers();
        var toastNotificationManagerForUser = ToastNotificationManager.GetDefault();
        _toastNotifier = toastNotificationManagerForUser.CreateToastNotifier("Kitopia");
    }

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
            if (windowFromIntPtr == null)
            {
                Log.Warning("无法通过前台窗口句柄获取Avalonia窗口，Toast显示失败");
                return;
            }

            var windowToastManager = WindowToastManager.TryGetToastManager(windowFromIntPtr, out var manager)
                ? manager
                : new WindowToastManager(windowFromIntPtr);
            windowToastManager!.Show(
                new Toast($"{header}{text}"),
                showIcon: true,
                showClose: true,
                type: notificationType);
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