using Avalonia.Media.Imaging;

namespace Kitopia.Desktop.Features.Search.Preview;

public sealed record FilePreviewContent(
    string Name,
    string Details,
    Bitmap? Image = null,
    string? Text = null,
    IReadOnlyList<FilePreviewEntry>? Entries = null,
    string? NativePath = null,
    string? Notice = null,
    string? CurrentDirectoryPath = null,
    bool RequiresArchiveExtraction = false,
    string? ExtractedPath = null);

public sealed record FilePreviewEntry(
    string Name,
    string Details,
    bool IsDirectory,
    string? TargetPath = null,
    string? ArchiveEntryPath = null);
