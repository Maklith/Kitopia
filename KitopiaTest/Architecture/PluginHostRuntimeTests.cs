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
}
