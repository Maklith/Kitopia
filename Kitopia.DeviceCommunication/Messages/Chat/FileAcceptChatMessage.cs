namespace Kitopia.DeviceCommunication.Messages.Chat;

public sealed record FileAcceptChatMessage(string ConversationId, Guid TransferId)
    : AppMessage(ConversationId);
