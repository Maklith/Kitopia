using System.Text.Json;
using Kitopia.Desktop.Features.Services.Plugin;

namespace KitopiaTest.Services;

[TestClass]
public sealed class PluginContractTests
{
    [TestMethod]
    public void PluginManifest_ConvertsOnlyRuntimeFields()
    {
        const string json = """
            {
              "Id": 42,
              "AuthorId": 7,
              "IsPublic": true,
              "VersionId": 3,
              "Name": "Example",
              "NameSign": "example",
              "Version": "1.2.0",
              "Description": "Example plugin",
              "Main": "Example.dll",
              "Dependencies": { "Kitopia": "[1.0.0,2.0.0)" },
              "OptionalDependencies": { "codec": "[1.0.0,2.0.0)" },
              "OptionalDependencyDescriptions": { "codec": "Compression features" }
            }
            """;

        var manifest = System.Text.Json.JsonSerializer.Deserialize<PluginManifest>(json);
        var plugin = manifest!.ToPluginBaseInfo();

        Assert.AreEqual("Example", plugin.Name);
        Assert.AreEqual("example", plugin.NameSign);
        Assert.AreEqual("1.2.0", plugin.Version);
        Assert.AreEqual("Example.dll", plugin.Main);
        Assert.AreEqual("[1.0.0,2.0.0)", plugin.Dependencies["Kitopia"]);
        Assert.AreEqual("[1.0.0,2.0.0)", plugin.OptionalDependencies!["codec"]);
        Assert.AreEqual("Compression features", plugin.OptionalDependencyDescriptions!["codec"]);
        Assert.AreEqual(0, plugin.Id);
        Assert.AreEqual(0, plugin.VersionId);
    }

    [TestMethod]
    public void PluginManifest_LegacyManifest_HasNoOptionalDependencies()
    {
        const string json = """
            { "Name": "Legacy", "NameSign": "legacy", "Version": "1.0.0",
              "Description": "Legacy plugin", "Main": "Legacy.dll" }
            """;
        var plugin = System.Text.Json.JsonSerializer.Deserialize<PluginManifest>(json)!.ToPluginBaseInfo();
        Assert.IsEmpty(plugin.OptionalDependencies!);
        Assert.IsEmpty(plugin.OptionalDependencyDescriptions!);
    }

    [TestMethod]
    public void IsVersionNewer_UsesSemanticVersionPrecedence()
    {
        Assert.IsTrue(PluginDependencyService.IsVersionNewer("1.0.0", "1.0.0-rc.1"));
        Assert.IsTrue(PluginDependencyService.IsVersionNewer("1.0.0-rc.2", "1.0.0-rc.1"));
        Assert.IsFalse(PluginDependencyService.IsVersionNewer("1.0.0-rc.1", "1.0.0"));
        Assert.IsFalse(PluginDependencyService.IsVersionNewer("1.0.0+build.2", "1.0.0+build.1"));
        Assert.IsTrue(PluginDependencyService.IsVersionNewer("1.0.0-rc.10", "1.0.0-rc.2"));
        Assert.IsTrue(PluginDependencyService.IsVersionNewer("1.0.0.1", "1.0.0"));
        Assert.IsFalse(PluginDependencyService.IsVersionNewer("1.0.0.0", "1.0"));
        Assert.IsFalse(PluginDependencyService.IsVersionNewer("invalid", "1.0.0"));
    }

    [TestMethod]
    public void SupportsCurrentPlatform_HandlesMissingAndExplicitPlatformMetadata()
    {
        Assert.IsFalse(PluginNetworkService.SupportsCurrentPlatform([]));

        var currentPlatform = OperatingSystem.IsWindows()
            ? "windows"
            : OperatingSystem.IsMacOS()
                ? "macos"
                : "linux";
        Assert.IsTrue(PluginNetworkService.SupportsCurrentPlatform([currentPlatform.ToUpperInvariant()]));
        Assert.IsFalse(PluginNetworkService.SupportsCurrentPlatform(["unsupported-platform"]));
    }
}
