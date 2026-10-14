namespace Kitopia.Feature.DeviceCommunication.Application;

public readonly record struct ChatDisplayContext(bool IsMainWindowActive, bool IsChatPageOpen);

public interface IChatPlatformService
{
    bool CanOpenFile { get; }
    void OpenFile(string path);
    Task<string?> PromptTextAsync(string title, string prompt, string? initialValue);
    ChatDisplayContext GetDisplayContext(string? selectedConversationId);
}
