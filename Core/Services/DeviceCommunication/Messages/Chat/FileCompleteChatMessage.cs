namespace Core.Services.DeviceCommunication.Messages.Chat;

public sealed record FileCompleteChatMessage(string ConversationId, Guid TransferId)
    : Core.Services.DeviceCommunication.Messages.AppMessage(ConversationId);
