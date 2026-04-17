using Avalonia.Controls.Notifications;
using PluginCore;

namespace Core.Utils;

public static class DialogContentToastExtensions
{
    public static ToastRequest ToToastRequest(this DialogContent dialogContent,
        NotificationType notificationType = NotificationType.Information)
    {
        ArgumentNullException.ThrowIfNull(dialogContent);

        var actions = new List<ToastAction>();

        if (!string.IsNullOrWhiteSpace(dialogContent.PrimaryButtonText))
        {
            actions.Add(new ToastAction
            {
                Text = dialogContent.PrimaryButtonText!,
                Callback = dialogContent.PrimaryAction,
                IsPrimary = true
            });
        }

        if (!string.IsNullOrWhiteSpace(dialogContent.SecondaryButtonText))
        {
            actions.Add(new ToastAction
            {
                Text = dialogContent.SecondaryButtonText!,
                Callback = dialogContent.SecondaryAction
            });
        }

        if (!string.IsNullOrWhiteSpace(dialogContent.CloseButtonText))
        {
            actions.Add(new ToastAction
            {
                Text = dialogContent.CloseButtonText!,
                Callback = dialogContent.CloseAction
            });
        }

        return new ToastRequest
        {
            Header = dialogContent.Title,
            Text = dialogContent.Content?.ToString() ?? string.Empty,
            NotificationType = notificationType,
            AutoCloseDelay = null,
            Actions = actions,
            ShowCloseButton = actions.Count == 0
        };
    }
}
