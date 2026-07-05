using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Services.DeviceCommunication.Platform;

public readonly record struct ChatDisplayContext(bool IsMainWindowActive, bool IsChatPageOpen);

public sealed record ChatFileSaveTarget(
    string DisplayPath,
    string? LocalPath,
    Func<CancellationToken, ValueTask<Stream>> OpenWriteAsync)
{
    public static ChatFileSaveTarget FromLocalPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return new ChatFileSaveTarget(
            path,
            path,
            _ => new ValueTask<Stream>(new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                useAsync: true)));
    }
}

public interface IChatPlatformService
{
    Task<IReadOnlyList<string>> PickFilesToSendAsync();
    Task<ChatFileSaveTarget?> PickSaveTargetAsync(string suggestedFileName);
    bool CanOpenFile { get; }
    void OpenFile(string path);
    Task CopyTextToClipboardAsync(string text);
    Task<string?> PromptTextAsync(string title, string prompt, string? initialValue);
    ChatDisplayContext GetDisplayContext(string? selectedConversationId);
}
