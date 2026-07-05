using Kitopia.Mobile.Services;

namespace KitopiaTest.Mobile;

[TestClass]
public sealed class MobileReceiveSavePathResolverTests
{
    [TestMethod]
    public void ResolveIncomingPath_UsesAppIncomingFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), $"kitopia-mobile-save-{Guid.NewGuid():N}");

        try
        {
            var path = MobileReceiveSavePathResolver.ResolveIncomingPath(root, "sample.bin");

            Assert.AreEqual(
                Path.Combine(root, "Incoming", "sample.bin"),
                path);
            Assert.IsTrue(Directory.Exists(Path.Combine(root, "Incoming")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public void ResolveIncomingPath_SanitizesUnsafeFileName()
    {
        var root = Path.Combine(Path.GetTempPath(), $"kitopia-mobile-save-{Guid.NewGuid():N}");

        try
        {
            var path = MobileReceiveSavePathResolver.ResolveIncomingPath(root, @"..\unsafe?.txt");

            Assert.AreEqual(
                Path.Combine(root, "Incoming", "unsafe_.txt"),
                path);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
