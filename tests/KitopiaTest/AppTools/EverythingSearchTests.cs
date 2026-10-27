using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Search;
using Kitopia.Desktop.Platform.Windows.Everything;
using PluginCore.Config;

namespace KitopiaTest.AppTools;

[TestClass]
[DoNotParallelize]
public sealed class EverythingSearchTests
{
    [TestMethod]
    [DataRow(EverythingSortOrder.NameAscending, 1u)]
    [DataRow(EverythingSortOrder.NameDescending, 2u)]
    [DataRow(EverythingSortOrder.PathAscending, 3u)]
    [DataRow(EverythingSortOrder.PathDescending, 4u)]
    [DataRow(EverythingSortOrder.SizeAscending, 5u)]
    [DataRow(EverythingSortOrder.SizeDescending, 6u)]
    [DataRow(EverythingSortOrder.DateModifiedAscending, 13u)]
    [DataRow(EverythingSortOrder.DateModifiedDescending, 14u)]
    public void Search_WithSortOrder_SetsEverythingSort(EverythingSortOrder sortOrder, uint expected)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Everything requires Windows.");
            return;
        }

        var previousConfigs = ConfigManger.Configs;
        ConfigManger.Configs = new Dictionary<string, ConfigBase> { ["KitopiaConfig"] = new KitopiaConfig() };
        try
        {
            _ = EverythingTools.Search("kitopia-sort-test-" + Guid.NewGuid().ToString("N"), 0, 1, sortOrder).ToArray();
            var sort = IntPtr.Size == 8 ? Everything64.Everything_GetSort() : Everything32.Everything_GetSort();
            Assert.AreEqual(expected, sort);
        }
        finally
        {
            if (IntPtr.Size == 8) Everything64.Everything_Reset();
            else Everything32.Everything_Reset();
            ConfigManger.Configs = previousConfigs;
        }
    }

    [TestMethod]
    public void Search_CaseSettingChanges_UpdatesEverythingMatchCaseOnNextQuery()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Everything requires Windows.");
            return;
        }

        var previousConfigs = ConfigManger.Configs;
        var config = new KitopiaConfig();
        ConfigManger.Configs = new Dictionary<string, ConfigBase> { ["KitopiaConfig"] = config };
        try
        {
            foreach (var ignoreCase in new[] { true, false, true })
            {
                config.everythingSearchIgnoreCase = ignoreCase;
                // A unique query avoids indexing real files and works without an Everything server.
                _ = EverythingTools.Search("kitopia-case-test-" + Guid.NewGuid().ToString("N"), 1).ToArray();
                var matchCase = IntPtr.Size == 8
                    ? Everything64.Everything_GetMatchCase()
                    : Everything32.Everything_GetMatchCase();
                Assert.AreEqual(!ignoreCase, matchCase);
            }
        }
        finally
        {
            if (IntPtr.Size == 8) Everything64.Everything_Reset();
            else Everything32.Everything_Reset();
            ConfigManger.Configs = previousConfigs;
        }
    }

    [TestMethod]
    public void Search_WithOffsetAndCount_SetsEverythingOffsetAndMax()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Everything requires Windows.");
            return;
        }

        var previousConfigs = ConfigManger.Configs;
        var config = new KitopiaConfig();
        ConfigManger.Configs = new Dictionary<string, ConfigBase> { ["KitopiaConfig"] = config };
        try
        {
            _ = EverythingTools.Search("kitopia-page-test-" + Guid.NewGuid().ToString("N"), 100, 50).ToArray();
            var offset = IntPtr.Size == 8
                ? Everything64.Everything_GetOffset()
                : Everything32.Everything_GetOffset();
            var maxResults = IntPtr.Size == 8
                ? Everything64.Everything_GetMax()
                : Everything32.Everything_GetMax();

            Assert.AreEqual(100u, offset);
            Assert.AreEqual(50u, maxResults);
        }
        finally
        {
            if (IntPtr.Size == 8) Everything64.Everything_Reset();
            else Everything32.Everything_Reset();
            ConfigManger.Configs = previousConfigs;
        }
    }
}
