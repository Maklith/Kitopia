using Avalonia.Controls.Notifications;
using PluginCore;

namespace Core.Services.DeviceCommunication.Presentation;

public sealed class ToastNotificationService : INotificationService
{
    private readonly IToastService _toastService;

    public ToastNotificationService(IToastService toastService)
    {
        _toastService = toastService;
    }

    public void Show(string title, string message, NotificationType type)
    {
        _toastService.Show(title, message, type);
    }

    public void Show(ToastRequest request)
    {
        _toastService.Show(request);
    }

    public IToastProgressHandle ShowProgress(
        string title,
        string message,
        NotificationType type,
        double initialProgress = 0,
        bool isIndeterminate = false)
    {
        return _toastService.ShowProgress(title, message, type, initialProgress, isIndeterminate);
    }
}
