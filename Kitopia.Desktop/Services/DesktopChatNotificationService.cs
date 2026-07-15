using System;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Kitopia.Feature.DeviceCommunication.Application;
using PluginCore;

namespace Kitopia.Desktop.Services;

public sealed class DesktopChatNotificationSink : IChatNotificationSink
{
    private readonly IToastService _toastService;

    public DesktopChatNotificationSink(IToastService toastService)
    {
        _toastService = toastService;
    }

    public bool IncomingMessagesHandledExternally => true;

    public Task ShowAsync(
        string header,
        string text,
        ChatNotificationKind kind = ChatNotificationKind.Information,
        bool persistent = false)
    {
        return _toastService.Show(new ToastRequest
        {
            Header = header,
            Text = text,
            NotificationType = kind switch
            {
                ChatNotificationKind.Success => NotificationType.Success,
                ChatNotificationKind.Warning => NotificationType.Warning,
                ChatNotificationKind.Error => NotificationType.Error,
                _ => NotificationType.Information
            },
            AutoCloseDelay = persistent ? null : TimeSpan.FromSeconds(5)
        });
    }
}
