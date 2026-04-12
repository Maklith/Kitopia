namespace Core.Services.DeviceCommunication.Messages.Chat;

public sealed record FileChatMessage(string ConversationId, Guid ChannelId, string FileName, long? Length)
    : Core.Services.DeviceCommunication.Messages.AppMessage(ConversationId);
