namespace Kitopia.Feature.DeviceCommunication.Messages.Chat;

public sealed record FileChatMessage(
    string ConversationId,
    Guid ChannelId,
    string FileName,
    long? Length,
    byte[]? IconPng = null)
    : AppMessage(ConversationId);
