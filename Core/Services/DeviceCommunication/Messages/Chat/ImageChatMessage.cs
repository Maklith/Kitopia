namespace Core.Services.DeviceCommunication.Messages.Chat;

public sealed record ImageChatMessage(string ConversationId, Guid ChannelId, string? ContentType)
    : Core.Services.DeviceCommunication.Messages.AppMessage(ConversationId);
