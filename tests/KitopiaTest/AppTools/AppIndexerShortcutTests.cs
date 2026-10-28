using Kitopia.Desktop.Platform.Windows.AppTools;

namespace KitopiaTest.AppTools;

[TestClass]
public sealed class AppIndexerShortcutTests
{
    [TestMethod]
    public void IsWindowsInstallerCachePath_InstallerResource_ReturnsTrue()
    {
        var installerResource = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Installer", "{6172DDB3-98E6-46E1-A0C1-DB8FD05063EF}", "i386_SldWorks.exe");

        Assert.IsTrue(AppIndexer.IsWindowsInstallerCachePath(installerResource));
    }

    [TestMethod]
    public void IsWindowsInstallerCachePath_RegularApplication_ReturnsFalse()
    {
        var executablePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Example", "Example.exe");

        Assert.IsFalse(AppIndexer.IsWindowsInstallerCachePath(executablePath));
    }

    [TestMethod]
    public void SearchEntryToSearchViewItem_ShortcutLaunchPath_IsPreserved()
    {
        const string shortcutPath = @"C:\Users\Public\Desktop\SOLIDWORKS Design 2026.lnk";
        var entry = new Kitopia.Desktop.Features.Search.SearchEntry
        {
            DisplayName = "SOLIDWORKS Design 2026",
            OnlyKey = shortcutPath,
            FileType = PluginCore.FileType.应用程序
        };

        Assert.AreEqual(shortcutPath, entry.OnlyKey);
        Assert.IsNull(entry.LaunchPath);
        Assert.IsNull(entry.Arguments);
        Assert.IsNull(entry.StartDirectory);
    }
}
