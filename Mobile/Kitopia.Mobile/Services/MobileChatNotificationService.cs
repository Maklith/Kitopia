using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using Kitopia.Feature.DeviceCommunication.Application;

namespace Kitopia.Mobile.Services;

public sealed class MobileChatNotificationSink : IChatNotificationSink
{
    private readonly MobileTopLevelContext _topLevel;

    public MobileChatNotificationSink(MobileTopLevelContext topLevel)
    {
        _topLevel = topLevel;
    }

    public async Task ShowAsync(
        string header,
        string text,
        ChatNotificationKind kind = ChatNotificationKind.Information,
        bool persistent = false)
    {
        var nativeNotifier = NativeNotifier.Current;
        if (nativeNotifier is not null)
        {
            nativeNotifier.Show(header, text);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var topLevel = _topLevel.CurrentTopLevel;
            if (topLevel is null)
            {
                return;
            }

            var manager = new WindowNotificationManager(topLevel)
            {
                Position = NotificationPosition.BottomCenter,
                MaxItems = 3
            };
            manager.Show(new Notification(
                header,
                text,
                MapKind(kind),
                expiration: persistent ? null : TimeSpan.FromSeconds(5)));
        });
    }

    private static NotificationType MapKind(ChatNotificationKind kind)
    {
        return kind switch
        {
            ChatNotificationKind.Success => NotificationType.Success,
            ChatNotificationKind.Warning => NotificationType.Warning,
            ChatNotificationKind.Error => NotificationType.Error,
            _ => NotificationType.Information
        };
    }
}
