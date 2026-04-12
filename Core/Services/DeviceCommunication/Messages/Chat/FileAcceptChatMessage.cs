namespace Core.Services.DeviceCommunication.Messages.Chat;

public sealed record FileAcceptChatMessage(string ConversationId, Guid TransferId)
    : Core.Services.DeviceCommunication.Messages.AppMessage(ConversationId);
