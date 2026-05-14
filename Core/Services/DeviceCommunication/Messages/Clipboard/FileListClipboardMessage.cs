namespace Core.Services.DeviceCommunication.Messages.Clipboard;

public sealed record FileListClipboardMessage(string ConversationId, IReadOnlyList<string> Paths)
    : AppMessage(ConversationId);
