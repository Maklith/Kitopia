using Core.Services.DeviceCommunication.Messages;

namespace Core.Services.DeviceCommunication.Application;

public enum IncomingMessageEventType
{
    Message = 1,
    FileOffer = 2,
    TransferProgress = 3,
    TransferCompleted = 4,
    TransferRejected = 5,
    TransferCancelled = 6,
    TransferTimeout = 7
}

public sealed record IncomingMessageEvent(
    AppMessage Message,
    IncomingMessageEventType EventType = IncomingMessageEventType.Message,
    Guid? TransferId = null,
    long? BytesTransferred = null,
    long? TotalBytes = null,
    string? Reason = null);
