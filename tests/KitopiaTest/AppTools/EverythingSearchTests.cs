using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Platform.Windows.Everything;
using PluginCore.Config;

namespace KitopiaTest.AppTools;

[TestClass]
[DoNotParallelize]
public sealed class EverythingSearchTests
{
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
}
