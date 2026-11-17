using Kitopia.Desktop.Platform.Linux;

namespace KitopiaTest.Architecture;

[TestClass]
public sealed class DesktopAbstractionsTests
{
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
