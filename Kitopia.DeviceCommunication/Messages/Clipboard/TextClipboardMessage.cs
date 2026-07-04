namespace Kitopia.DeviceCommunication.Messages.Clipboard;

public sealed record TextClipboardMessage(string ConversationId, string Text)
    : AppMessage(ConversationId);
