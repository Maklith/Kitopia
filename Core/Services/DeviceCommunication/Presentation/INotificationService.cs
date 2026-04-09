using Avalonia.Controls.Notifications;
using PluginCore;

namespace Core.Services.DeviceCommunication.Presentation;

public interface INotificationService
{
    void Show(string title, string message, NotificationType type);
    void Show(ToastRequest request);
    IToastProgressHandle ShowProgress(
        string title,
        string message,
        NotificationType type,
        double initialProgress = 0,
        bool isIndeterminate = false);
}
