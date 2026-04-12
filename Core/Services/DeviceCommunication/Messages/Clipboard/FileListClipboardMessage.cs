namespace Core.Services.DeviceCommunication.Messages.Clipboard;

public sealed record FileListClipboardMessage(string ConversationId, IReadOnlyList<string> Paths)
    : Core.Services.DeviceCommunication.Messages.AppMessage(ConversationId);
