namespace Kitopia.Desktop.Features.Services.Plugin;

public sealed class FeatureInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public required string Source { get; init; }
    public required int IconSymbol { get; init; }
    public required int Order { get; init; }
    public required Func<CancellationToken, Task> ExecuteAsync { get; init; }

    public string IconGlyph => char.ConvertFromUtf32(IconSymbol);
}
