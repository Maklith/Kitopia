namespace Core.Services.DeviceCommunication.Messages.Clipboard;

public sealed record ImageClipboardMessage(string ConversationId, Guid ChannelId, string? ContentType)
    : Core.Services.DeviceCommunication.Messages.AppMessage(ConversationId);
