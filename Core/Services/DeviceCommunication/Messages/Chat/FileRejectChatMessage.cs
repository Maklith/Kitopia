namespace Core.Services.DeviceCommunication.Messages.Chat;

public sealed record FileRejectChatMessage(string ConversationId, Guid TransferId, string Reason)
    : Core.Services.DeviceCommunication.Messages.AppMessage(ConversationId);
