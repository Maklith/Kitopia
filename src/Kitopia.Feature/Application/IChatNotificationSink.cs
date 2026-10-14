namespace Kitopia.Feature.DeviceCommunication.Application;

public enum ChatNotificationKind
{
    Information,
    Success,
    Warning,
    Error
}

public interface IChatNotificationSink
{
    bool IncomingMessagesHandledExternally => false;

    Task ShowAsync(
        string header,
        string text,
        ChatNotificationKind kind = ChatNotificationKind.Information,
        bool persistent = false);
}
