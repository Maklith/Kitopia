namespace Kitopia.DeviceCommunication.Messages.Chat;

public sealed record FileChatMessage(string ConversationId, Guid ChannelId, string FileName, long? Length)
    : AppMessage(ConversationId);
