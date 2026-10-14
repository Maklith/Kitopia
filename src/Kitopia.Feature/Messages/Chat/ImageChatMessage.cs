namespace Kitopia.Feature.DeviceCommunication.Messages.Chat;

public sealed record ImageChatMessage(
    string ConversationId,
    Guid TransferId,
    long SizeBytes,
    string? ContentType,
    bool IsDirect)
    : AppMessage(ConversationId);
