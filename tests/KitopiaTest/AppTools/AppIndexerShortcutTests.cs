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
}
