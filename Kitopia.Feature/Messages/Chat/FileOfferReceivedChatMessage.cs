namespace Kitopia.Feature.DeviceCommunication.Messages.Chat;

public sealed record FileOfferReceivedChatMessage(string ConversationId, Guid TransferId)
    : AppMessage(ConversationId);
