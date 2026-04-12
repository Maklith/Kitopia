namespace Core.Services.DeviceCommunication.Messages.Clipboard;

public sealed record TextClipboardMessage(string ConversationId, string Text)
    : Core.Services.DeviceCommunication.Messages.AppMessage(ConversationId);
