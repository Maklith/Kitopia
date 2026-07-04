namespace Kitopia.DeviceCommunication.Messages.Chat;

public sealed record FileCompleteChatMessage(string ConversationId, Guid TransferId)
    : AppMessage(ConversationId);
