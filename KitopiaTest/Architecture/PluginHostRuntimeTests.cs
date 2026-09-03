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

    [TestMethod]
    public void PluginInfoUiHelper_WebCardPresentationProperties_FormatExpectedValues()
    {
        var helper = new PluginInfoUiHelper
        {
            PluginBaseInfo = new PluginBaseInfo
            {
                Name = "天气小组件",
                NameSign = "weather_show",
                Version = "1.0.0",
                Description = "一个桌面小组件用于显示天气"
            },
            OnlinePluginInfo = new OnlinePluginInfo
            {
                Name = "天气小组件",
                NameSign = "weather_show",
                LastVersion = "1.0.0",
                AuthorNickname = "Maklith",
                PublicationStatus = 2,
                AvailablePlatforms = ["windows"],
                DownloadCounts = 6,
                Updatetime = new DateTime(2026, 8, 28)
            },
            IsLocal = false,
            AuthorName = "Maklith"
        };

        Assert.AreEqual("天", helper.PluginInitial);
        Assert.AreEqual("M", helper.AuthorInitial);
        Assert.AreEqual("公开", helper.PublicationStatusText);
        Assert.AreEqual("v1.0.0 · 8月28日", helper.VersionAndDateText);
        Assert.AreEqual("6 下载", helper.DownloadCountText);
        CollectionAssert.AreEqual(new[] { "Windows" }, (System.Collections.ICollection)helper.DisplayPlatforms);
    }
}
