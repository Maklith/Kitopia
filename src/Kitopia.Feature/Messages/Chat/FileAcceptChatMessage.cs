namespace Kitopia.Feature.DeviceCommunication.Messages.Chat;

public sealed record FileAcceptChatMessage(string ConversationId, Guid TransferId)
    : AppMessage(ConversationId);
