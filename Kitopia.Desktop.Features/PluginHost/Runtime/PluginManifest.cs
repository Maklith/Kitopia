using PluginCore;

namespace Kitopia.Desktop.Features.Services.Plugin;

public sealed class PluginManifest
{
    public required string Name { get; init; }
    public required string NameSign { get; init; }
    public required string Version { get; init; }
    public required string Description { get; init; }
    public required string Main { get; init; }
    public Dictionary<string, string> Dependencies { get; init; } = [];

    public PluginBaseInfo ToPluginBaseInfo() => new()
    {
        Name = Name,
        NameSign = NameSign,
        Version = Version,
        Description = Description,
        Main = Main,
        Dependencies = Dependencies
    };
}
