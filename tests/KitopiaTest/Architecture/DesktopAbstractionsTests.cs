using Kitopia.Desktop.Abstractions;
using Kitopia.Desktop.Abstractions.FileSystem;
using Kitopia.Desktop.Abstractions.Shell;
using Kitopia.Desktop.Platform.Linux;
using Kitopia.Desktop.Platform.Windows;
using Kitopia.Desktop.Platform.Windows.AppTools;
using Kitopia.Desktop.Platform.Windows.Services;

namespace KitopiaTest.Architecture;

[TestClass]
public sealed class DesktopAbstractionsTests
{
    [TestMethod]
    public void Contracts_AreOwnedByPureAbstractionsAssembly()
    {
        var assembly = typeof(IDesktopShell).Assembly;

        Assert.AreEqual("Kitopia.Desktop.Abstractions", assembly.GetName().Name);
        Assert.AreSame(assembly, typeof(IFileLockService).Assembly);
        Assert.AreSame(assembly, typeof(FileLockInfo).Assembly);
        Assert.AreSame(assembly, typeof(IDesktopPlatformInfo).Assembly);
    }

    [TestMethod]
    public void PlatformProjects_ProvideBothDesktopImplementations()
    {
        Assert.IsTrue(typeof(IDesktopShell).IsAssignableFrom(typeof(ShellUtils)));
        Assert.IsTrue(typeof(IFileLockService).IsAssignableFrom(typeof(FileLocksmithService)));
        Assert.IsTrue(typeof(IDesktopPlatformInfo).IsAssignableFrom(typeof(WindowsDesktopPlatformInfo)));

        Assert.IsTrue(typeof(IDesktopShell).IsAssignableFrom(typeof(LinuxDesktopShell)));
        Assert.IsTrue(typeof(IFileLockService).IsAssignableFrom(typeof(LinuxFileLockService)));
        Assert.IsTrue(typeof(IDesktopPlatformInfo).IsAssignableFrom(typeof(LinuxDesktopPlatformInfo)));
    }

    [TestMethod]
    public async Task LinuxFileLockService_EmptyRequest_ReturnsEmptyResult()
    {
        var service = new LinuxFileLockService();

        var result = await service.CheckFileLocksAsync([]);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task LinuxFileLockService_InvalidInput_ThrowsActionableException()
    {
        var service = new LinuxFileLockService();

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.CheckFileLocksAsync([" "]));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.UnlockFileAsync([0]));
    }
}
