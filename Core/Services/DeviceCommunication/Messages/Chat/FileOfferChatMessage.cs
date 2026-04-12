namespace Core.Services.DeviceCommunication.Messages.Chat;

public sealed record FileOfferChatMessage(
    string ConversationId,
    Guid TransferId,
    string FileName,
    long SizeBytes,
    string? ContentType,
    string? Hash = null)
    : Core.Services.DeviceCommunication.Messages.AppMessage(ConversationId);
