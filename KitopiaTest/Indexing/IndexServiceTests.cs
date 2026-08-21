using Kitopia.Desktop.Features.Indexing;
using Kitopia.Desktop.Features.Services.Config;
using PluginCore.Config;

namespace KitopiaTest.Indexing;

[TestClass]
[DoNotParallelize]
public sealed class IndexServiceTests
{
    private Dictionary<string, ConfigBase>? _originalConfigs;

    [TestInitialize]
    public void Initialize()
    {
        _originalConfigs = ConfigManger.Configs;
        ConfigManger.Configs = new Dictionary<string, ConfigBase>
        {
            ["KitopiaConfig"] = new KitopiaConfig { Name = "KitopiaConfig" }
        };
    }

    [TestCleanup]
    public void Cleanup()
    {
        ConfigManger.Configs = _originalConfigs!;
    }

    [TestMethod]
    public void ShouldAutomaticallyIndexFile_DefaultNamesExcludeMinecraftAssets()
    {
        var path = Path.Combine("root", ".minecraft", "assets", "readme.md");

        Assert.IsFalse(IndexService.ShouldAutomaticallyIndexFile(path));
        Assert.IsFalse(IndexService.ShouldAutomaticallyIndexEverythingFile(path));
        Assert.IsFalse(IndexService.ShouldAutomaticallyIndexFile(
            Path.Combine("root", "assets", "readme.md")));
    }

    [TestMethod]
    public void ShouldAutomaticallyIndexFile_DefaultExtensionsExcludeUnwantedFiles()
    {
        Assert.IsFalse(IndexService.ShouldAutomaticallyIndexFile(Path.Combine("root", "program.exe")));
        Assert.IsFalse(IndexService.ShouldAutomaticallyIndexFile(Path.Combine("root", "state.dat")));
        Assert.IsTrue(IndexService.ShouldAutomaticallyIndexFile(Path.Combine("root", "manual.pdf")));
    }

    [TestMethod]
    public void ShouldAutomaticallyIndexFile_UsesConfiguredNames()
    {
        ConfigManger.Config.transientDirectoryNames.Clear();
        ConfigManger.Config.transientDirectoryNames.Add("generated");

        Assert.IsFalse(IndexService.ShouldAutomaticallyIndexFile(
            Path.Combine("root", "generated", "file.pdf")));
        Assert.IsTrue(IndexService.ShouldAutomaticallyIndexFile(
            Path.Combine("root", "cache", "file.pdf")));
    }

    [TestMethod]
    public void ShouldAutomaticallyIndexFile_UsesConfiguredExtensions()
    {
        ConfigManger.Config.allowedFileExtensions.Clear();
        ConfigManger.Config.allowedFileExtensions.Add("*.txt");

        Assert.IsTrue(IndexService.ShouldAutomaticallyIndexFile(Path.Combine("root", "notes.txt")));
        Assert.IsFalse(IndexService.ShouldAutomaticallyIndexFile(Path.Combine("root", "notes.pdf")));
    }

    [TestMethod]
    public void ShouldAutomaticallyIndexEverythingFile_IgnoresAllowedExtensions()
    {
        ConfigManger.Config.allowedFileExtensions.Clear();
        ConfigManger.Config.allowedFileExtensions.Add("*.pdf");

        Assert.IsFalse(IndexService.ShouldAutomaticallyIndexFile(Path.Combine("root", "notes.txt")));
        Assert.IsTrue(IndexService.ShouldAutomaticallyIndexEverythingFile(Path.Combine("root", "notes.txt")));
    }
}
