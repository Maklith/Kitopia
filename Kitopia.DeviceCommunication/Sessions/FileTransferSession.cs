namespace Kitopia.DeviceCommunication.Sessions;

public enum FileTransferState
{
    Offered = 1,
    Accepted = 2,
    Rejected = 3,
    Cancelled = 4,
    Completed = 5
}

public sealed class FileTransferSession
{
    public required string ConversationId { get; init; }
    public required Guid TransferId { get; init; }
    public required string FileName { get; init; }
    public required long SizeBytes { get; init; }
    public string? ContentType { get; init; }
    public FileTransferState State { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? SavePath { get; set; }
}
