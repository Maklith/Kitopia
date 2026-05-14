namespace Core.Services.DeviceCommunication.Messages.Chat;

public sealed record FileCancelChatMessage(string ConversationId, Guid TransferId, string Reason)
    : AppMessage(ConversationId);
