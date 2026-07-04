using Core.Services.DeviceCommunication.Messages;
using Core.Services.DeviceCommunication.Messages.Chat;

namespace Core.Services.DeviceCommunication.Application;

public abstract record DeviceMessageEvent(
    string ConversationId,
    DateTimeOffset TimestampUtc);

public sealed record ChatMessageReceivedEvent(
    AppMessage Message,
    byte[]? PayloadBytes,
    string ConversationId,
    DateTimeOffset TimestampUtc) : DeviceMessageEvent(ConversationId, TimestampUtc);

public enum FileTransferDirection
{
    Upload = 1,
    Download = 2
}

public enum FileTransferStatus
{
    WaitingForAccept = 1,
    Accepted = 2,
    InProgress = 3,
    Completed = 4,
    Rejected = 5,
    Cancelled = 6,
    Failed = 7,
    Timeout = 8,
    Delivered = 9
}

public sealed record FileTransferUpdatedEvent(
    string ConversationId,
    Guid TransferId,
    FileTransferDirection Direction,
    FileTransferStatus Status,
    string? FileName,
    long? BytesTransferred,
    long? TotalBytes,
    string? Reason,
    DateTimeOffset TimestampUtc,
    byte[]? IconPng = null) : DeviceMessageEvent(ConversationId, TimestampUtc);

public static class DeviceMessageEventFactory
{
    public static DeviceMessageEvent FromMessage(
        AppMessage message,
        byte[]? payloadBytes = null,
        DateTimeOffset? timestampUtc = null)
    {
        var timestamp = timestampUtc ?? DateTimeOffset.UtcNow;
        return message switch
        {
            FileOfferChatMessage fileOffer => new FileTransferUpdatedEvent(
                fileOffer.ConversationId,
                fileOffer.TransferId,
                FileTransferDirection.Download,
                FileTransferStatus.WaitingForAccept,
                fileOffer.FileName,
                null,
                fileOffer.SizeBytes,
                null,
                timestamp,
                fileOffer.IconPng),
            FileAcceptChatMessage fileAccept => new FileTransferUpdatedEvent(
                fileAccept.ConversationId,
                fileAccept.TransferId,
                FileTransferDirection.Upload,
                FileTransferStatus.Accepted,
                null,
                null,
                null,
                null,
                timestamp),
            FileRejectChatMessage fileReject => new FileTransferUpdatedEvent(
                fileReject.ConversationId,
                fileReject.TransferId,
                FileTransferDirection.Upload,
                ResolveRejectedStatus(fileReject.Reason),
                null,
                null,
                null,
                fileReject.Reason,
                timestamp),
            FileCancelChatMessage fileCancel => new FileTransferUpdatedEvent(
                fileCancel.ConversationId,
                fileCancel.TransferId,
                FileTransferDirection.Upload,
                FileTransferStatus.Cancelled,
                null,
                null,
                null,
                fileCancel.Reason,
                timestamp),
            FileCompleteChatMessage fileComplete => new FileTransferUpdatedEvent(
                fileComplete.ConversationId,
                fileComplete.TransferId,
                FileTransferDirection.Upload,
                FileTransferStatus.Completed,
                null,
                null,
                null,
                null,
                timestamp),
            _ => new ChatMessageReceivedEvent(message, payloadBytes, message.ConversationId, timestamp)
        };
    }

    private static FileTransferStatus ResolveRejectedStatus(string? reason)
    {
        return reason switch
        {
            "timeout" => FileTransferStatus.Timeout,
            "cancelled" => FileTransferStatus.Cancelled,
            "rejected_by_peer" or "rejected_by_user" => FileTransferStatus.Rejected,
            _ => FileTransferStatus.Failed
        };
    }
}
