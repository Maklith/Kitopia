namespace Kitopia.Desktop.Features.Indexing;

public sealed record IndexStatusSnapshot(
    int TotalEntries,
    int ApplicationEntries,
    int DocumentEntries,
    int ImageEntries,
    int TextVectorEntries,
    int ImageVectorEntries,
    int PendingImages,
    int ProcessingImages,
    int FailedImages,
    bool IsRebuilding,
    bool IsPaused,
    int TotalFileItems,
    int CompletedFileItems,
    string TextModel,
    string ImageModel,
    string? CurrentOperation,
    string? CurrentItem,
    string? LastError,
    DateTimeOffset UpdatedAt)
{
    public static IndexStatusSnapshot Empty { get; } = new(
        0, 0, 0, 0, 0, 0, 0, 0, 0, false, false, 0, 0,
        "BGE small zh INT8", "Chinese-CLIP RN50 INT8", null, null, null, DateTimeOffset.UtcNow);
}
