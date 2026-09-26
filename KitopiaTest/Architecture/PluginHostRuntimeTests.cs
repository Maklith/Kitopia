using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Plugin;
using PluginCore;
using PluginCore.Config;

namespace KitopiaTest.Architecture;

[TestClass]
public sealed class PluginHostRuntimeTests
{
    [TestMethod]
    [DataRow("1.2.0", "1.0.0", true)]
    [DataRow("2.0.0", "1.0.0", true)]
    [DataRow("0.9.0", "1.0.0", false)]
    [DataRow("1.0.0", "[1.0]", true)]
    [DataRow("1.0.0+build.2", "[1.0.0+build.1]", true)]
    [DataRow("1.0.0.1", "[1.0.0]", false)]
    [DataRow("1.0.1", "[1.0.0]", false)]
    [DataRow("1.0.0", "[1.0.0,2.0.0)", true)]
    [DataRow("2.0.0", "[1.0.0,2.0.0)", false)]
    [DataRow("1.0.0", "(1.0.0,2.0.0]", false)]
    [DataRow("2.0.0", "(1.0.0,2.0.0]", true)]
    [DataRow("0.9.0", "(,1.0.0)", true)]
    [DataRow("1.0.0", "(,1.0.0)", false)]
    [DataRow("2.0.0", "[1.0.0,)", true)]
    [DataRow("1.2.5", "1.2.*", true)]
    [DataRow("1.3.0", "1.2.*", false)]
    [DataRow("1.2.5-beta.1", "1.2.*", false)]
    [DataRow("1.2.5-beta.1", "1.2.*-*", true)]
    [DataRow("1.3.0-beta.1", "1.2.*-*", false)]
    [DataRow("1.2.3-rc.2", "1.2.3-rc.*", true)]
    [DataRow("1.2.3-beta.2", "1.2.3-rc.*", false)]
    [DataRow("1.0.0-beta.2", "[1.0.0-beta.2]", true)]
    [DataRow("1.0.0-BETA.2", "[1.0.0-beta.2]", true)]
    [DataRow("1.5.0-beta.2", "[1.0.0,2.0.0)", false)]
    [DataRow("1.5.0-beta.2", "[1.0.0-beta.1,2.0.0)", true)]
    [DataRow("1.5.0-beta.2", "(,2.0.0-beta.1)", true)]
    [DataRow("0.3.5.1", "*", true)]
    [DataRow("2.0.0", " * ", true)]
    [DataRow("1.0.0-beta.1", "*", false)]
    [DataRow("1.0.0-beta.1", "*-*", true)]
    [DataRow("invalid", "*", false)]
    [DataRow("1.0.0", "", false)]
    [DataRow("1.0.0", "^1.0.0", false)]
    [DataRow("1.0.0", "1.0.0 - 2.0.0", false)]
    [DataRow("1.0.0", ">=1.0.0", false)]
    public void VersionInRange_NuGetRangesAndPrereleasePolicy_AreEnforced(string version, string range, bool expected)
        => Assert.AreEqual(expected, PluginDependencyService.VersionInRange(version, range));

    [TestMethod]
    [DataRow("1.0.0", "1.2.0")]
    [DataRow("[1.0.0,2.0.0)", "1.2.0")]
    [DataRow("[1.2.5]", "1.2.5")]
    [DataRow("1.2.*", "1.2.5")]
    [DataRow("1.*", "1.5.0")]
    [DataRow("*", "2.0.0")]
    [DataRow("*-*", "3.0.0-beta.1")]
    [DataRow("1.2.*-*", "1.2.6-rc.1")]
    [DataRow("[1.2.6-rc.1,2.0.0)", "1.2.6-rc.1")]
    [DataRow("[1.9.0]", null)]
    [DataRow("4.*", null)]
    [DataRow("^1.0.0", null)]
    public void SelectDependencyVersion_UsesLowestOrFloatingHighestMatch(string range, string? expected)
    {
        string[] versions = ["1.5.0", "invalid", "3.0.0-beta.1", "1.2.5", "2.0.0", "1.2.6-rc.1", "1.2.0"];

        Assert.AreEqual(expected, PluginDependencyService.SelectDependencyVersion(versions, range));
        Assert.AreEqual(expected, PluginDependencyService.SelectDependencyVersion(versions.Reverse(), range));
    }

    [TestMethod]
    public void SelectDependencyVersion_SharedDependency_SatisfiesEveryRange()
    {
        string[] versions = ["1.1.0", "1.5.0", "2.0.0"];

        Assert.AreEqual("1.5.0", PluginDependencyService.SelectDependencyVersion(
            versions, ["[1.0.0,2.0.0)", "[1.5.0,3.0.0)"]));
        Assert.IsNull(PluginDependencyService.SelectDependencyVersion(
            versions, ["[1.0.0,1.5.0)", "[1.5.0,3.0.0)"]));
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow("0.3.5.1", "*", true)]
    [DataRow("0.3.5.1", "[0.3.0,0.4.0)", true)]
    [DataRow("0.3.5.1", "[0.3.5.1]", true)]
    [DataRow("0.3.5.1", "0.3.0", true)]
    [DataRow("0.3.5.1", "[0.2.0,0.3.0)", false)]
    [DataRow("0.3.5.1", "[0.3.5.0]", false)]
    [DataRow("0.3.5.1", "1.0.0", false)]
    [DataRow("0.3.5.1", "invalid", false)]
    public void CheckDependencies_HostVersion_UsesNuGetRanges(string version, string range, bool expected)
    {
        var previousVersion = ConfigManger.Version;
        ConfigManger.Version = new Version(version);
        try
        {
            var info = new PluginLocalInfo
            {
                PluginBaseInfo = new PluginBaseInfo
                {
                    Name = "Test plugin", NameSign = "test", Version = "1.0.0",
                    Dependencies = new Dictionary<string, string> { ["Kitopia"] = range }
                }
            };
            var (canLoad, results) = PluginDependencyService.CheckDependencies([], info.PluginBaseInfo.Dependencies, []);
            Assert.AreEqual(expected, canLoad);
            if (expected)
            {
                Assert.IsEmpty(results);
                PluginManager.ValidateDependencies(info, [], []);
            }
            else
            {
                Assert.AreEqual(PluginDependencyService.VersionCheckResult.Kitopia版本不匹配, results["Kitopia"]);
                var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
                    PluginManager.ValidateDependencies(info, [], []));
                StringAssert.Contains(exception.Message, $"当前 {version}");
                StringAssert.Contains(exception.Message, $"要求 {range}");
            }
        }
        finally
        {
            ConfigManger.Version = previousVersion;
        }
    }

    [TestMethod]
    public void CheckDependencies_InstalledOutsideFloatingRange_ReportsMismatch()
    {
        var available = new PluginBaseInfo
        {
            Name = "Dependency", NameSign = "dependency", Version = "0.3.5.1", Dependencies = new()
        };
        var (canLoad, results) = PluginDependencyService.CheckDependencies(
            [available], new Dictionary<string, string> { ["dependency"] = "0.2.*" }, ["dependency"]);

        Assert.IsFalse(canLoad);
        Assert.AreEqual(PluginDependencyService.VersionCheckResult.依赖版本不匹配, results["dependency"]);
    }

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
            new Dictionary<string, string> { ["dependency"] = "[1.0.0,2.0.0)" },
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
            new Dictionary<string, string> { ["dependency"] = "[1.0.0,2.0.0)" },
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

    [TestMethod]
    public void MarketPageViewModel_PaginationAndPlatformOptions_InitializeCorrectly()
    {
        var vm = new Kitopia.Desktop.Features.ViewModel.Pages.MarketPageViewModel();
        Assert.AreEqual(4, vm.PlatformOptions.Count);
        Assert.AreEqual("全部平台", vm.PlatformOptions[0].Label);
        Assert.AreEqual("", vm.PlatformOptions[0].Value);
        Assert.AreEqual("Windows", vm.PlatformOptions[1].Label);
        Assert.AreEqual("windows", vm.PlatformOptions[1].Value);

        Assert.AreEqual(1, vm.CurrentPage);
        Assert.IsFalse(vm.CanPreviousPage);
        Assert.AreEqual("1 / 1", vm.PageDisplayText);

        // Test jump to page logic
        vm.TotalPages = 5;
        Assert.IsTrue(vm.CanNextPage);
        Assert.IsTrue(vm.HasMultiplePages);

        vm.TargetPageText = "3";
        vm.JumpToPageCommand.Execute(null);
        Assert.AreEqual(3, vm.CurrentPage);
        Assert.IsTrue(vm.CanPreviousPage);
        Assert.IsTrue(vm.CanNextPage);
        Assert.AreEqual("3 / 5", vm.PageDisplayText);

        vm.NextPageCommand.Execute(null);
        Assert.AreEqual(4, vm.CurrentPage);

        vm.PreviousPageCommand.Execute(null);
        Assert.AreEqual(3, vm.CurrentPage);

        // Test search by author
        var pluginItem = new Kitopia.Desktop.Features.Services.Plugin.PluginInfoUiHelper
        {
            PluginBaseInfo = new PluginCore.PluginBaseInfo { Name = "Test", NameSign = "test_plugin" },
            OnlinePluginInfo = new Kitopia.Desktop.Features.Services.Plugin.OnlinePluginInfo
            {
                AuthorUserName = "Maklith",
                AuthorNickname = "马克里斯"
            },
            IsLocal = false,
            AuthorName = "马克里斯"
        };
        vm.SearchAuthorCommand.Execute(pluginItem);
        Assert.AreEqual("@Maklith", vm.Keyword);
        Assert.AreEqual(1, vm.CurrentPage);
    }

    [TestMethod]
    public void PluginNetworkService_CreateAuthorizedGetRequest_AttachesBearerTokenWhenPresent()
    {
        var originalConfigs = Kitopia.Desktop.Features.Services.Config.ConfigManger.Configs;
        try
        {
            var config = new Kitopia.Desktop.Features.Services.Config.KitopiaConfig();
            Kitopia.Desktop.Features.Services.Config.ConfigManger.Configs = new Dictionary<string, ConfigBase>
            {
                ["KitopiaConfig"] = config
            };

            // Case 1: No token
            config.userToken = string.Empty;
            using var anonymousReq = Kitopia.Desktop.Features.Services.Plugin.PluginNetworkService.CreateAuthorizedGetRequest("all");
            Assert.IsNull(anonymousReq.Headers.Authorization);

            // Case 2: Token present
            config.userToken = "test_desktop_token_12345";
            using var authReq = Kitopia.Desktop.Features.Services.Plugin.PluginNetworkService.CreateAuthorizedGetRequest("all");
            Assert.IsNotNull(authReq.Headers.Authorization);
            Assert.AreEqual("Bearer", authReq.Headers.Authorization.Scheme);
            Assert.AreEqual("test_desktop_token_12345", authReq.Headers.Authorization.Parameter);
        }
        finally
        {
            Kitopia.Desktop.Features.Services.Config.ConfigManger.Configs = originalConfigs;
        }
    }
}
