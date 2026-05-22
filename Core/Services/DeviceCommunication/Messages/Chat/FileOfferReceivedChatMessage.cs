namespace Core.Services.DeviceCommunication.Messages.Chat;

public sealed record FileOfferReceivedChatMessage(string ConversationId, Guid TransferId)
    : AppMessage(ConversationId);
