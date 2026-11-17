using Kitopia.Mobile.Services;

namespace KitopiaTest.Mobile;

[TestClass]
public sealed class MobileChatPlatformServiceTests
{
    [TestMethod]
    public void GetDisplayContext_WhenTopLevelIsUnavailable_ReturnsInactiveContext()
    {
        var topLevel = new MobileTopLevelContext();
        var service = new MobileChatPlatformService(topLevel);

        var context = service.GetDisplayContext(selectedConversationId: "peer-1");

        Assert.IsFalse(context.IsMainWindowActive);
        Assert.IsFalse(context.IsChatPageOpen);
    }

    [TestMethod]
    public void MobileFileCache_CreatePath_PreservesSafeNameAndAvoidsCollisions()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), $"kitopia-mobile-cache-{Guid.NewGuid():N}");
        try
        {
            var first = MobileFileCache.CreatePath(cacheRoot, "report final.pdf");
            var second = MobileFileCache.CreatePath(cacheRoot, "report final.pdf");

            Assert.AreEqual("report final.pdf", Path.GetFileName(first));
            Assert.AreEqual("report final.pdf", Path.GetFileName(second));
            Assert.AreNotEqual(Path.GetDirectoryName(first), Path.GetDirectoryName(second));
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
            {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    [TestMethod]
    public void MobileFileCache_SanitizeFileName_RemovesTraversalAndInvalidCharacters()
    {
        var sanitized = MobileFileCache.SanitizeFileName(@"..\unsafe?.txt");

        Assert.AreEqual("unsafe_.txt", sanitized);
    }

}
