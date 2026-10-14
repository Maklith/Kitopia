namespace Kitopia.Feature.DeviceCommunication.Messages.Chat;

public sealed record FileRejectChatMessage(string ConversationId, Guid TransferId, string Reason)
    : AppMessage(ConversationId);
