namespace Kitopia.Desktop.Features.Services.Plugin;

public sealed class FeatureCategory
{
    public required string Name { get; init; }
    public required IReadOnlyList<FeatureInfo> Features { get; init; }
}
