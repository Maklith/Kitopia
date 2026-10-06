namespace Kitopia.Desktop.Abstractions.TextSelection;

public sealed record TextSelectionSnapshot(
    string Text,
    nint SourceWindow,
    string? ProcessName,
    TextSelectionBounds? Bounds);
