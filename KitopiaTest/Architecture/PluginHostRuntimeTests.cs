using Kitopia.Desktop.Features.Services.Plugin;
using PluginCore;

namespace KitopiaTest.Architecture;

[TestClass]
public sealed class PluginHostRuntimeTests
{
    [TestMethod]
    public void CheckDependencies_EnabledCompatibleDependency_CanLoad()
    {
        var available = new PluginBaseInfo
        {
            Name = "Dependency",
            NameSign = "dependency",
            Version = "1.2.0",
            Dependencies = new Dictionary<string, string>()
        };

        var (canLoad, results) = PluginDependencyService.CheckDependencies(
            [available],
            new Dictionary<string, string> { ["dependency"] = "^1.0.0" },
            ["dependency"]);

        Assert.IsTrue(canLoad);
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void CheckDependencies_InstalledButDisabledDependency_ReportsDisabled()
    {
        var available = new PluginBaseInfo
        {
            Name = "Dependency",
            NameSign = "dependency",
            Version = "1.2.0",
            Dependencies = new Dictionary<string, string>()
        };

        var (canLoad, results) = PluginDependencyService.CheckDependencies(
            [available],
            new Dictionary<string, string> { ["dependency"] = "^1.0.0" },
            []);

        Assert.IsFalse(canLoad);
        Assert.AreEqual(
            PluginDependencyService.VersionCheckResult.依赖未启用,
            results["dependency"]);
    }
}
