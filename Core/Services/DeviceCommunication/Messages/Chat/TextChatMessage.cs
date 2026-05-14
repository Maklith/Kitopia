namespace Core.Services.DeviceCommunication.Messages.Chat;

public sealed record TextChatMessage(string ConversationId, string Text)
    : AppMessage(ConversationId);
