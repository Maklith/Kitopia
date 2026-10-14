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
    public async Task PromptTextAsync_WhenOverlayCompletes_ReturnsEnteredText()
    {
        var topLevel = new MobileTopLevelContext
        {
            TextPromptHandler = request =>
            {
                Assert.AreEqual("修改备注", request.Title);
                Assert.AreEqual("请输入备注名", request.Prompt);
                Assert.AreEqual("旧名称", request.InitialValue);
                request.TryComplete("新名称");
                return Task.CompletedTask;
            }
        };
        var service = new MobileChatPlatformService(topLevel);

        var result = await service.PromptTextAsync("修改备注", "请输入备注名", "旧名称");

        Assert.AreEqual("新名称", result);
    }

    [TestMethod]
    public async Task PromptTextAsync_WhenOverlayCancels_ReturnsNull()
    {
        var topLevel = new MobileTopLevelContext
        {
            TextPromptHandler = request =>
            {
                request.TryCancel();
                return Task.CompletedTask;
            }
        };
        var service = new MobileChatPlatformService(topLevel);

        var result = await service.PromptTextAsync("修改备注", "请输入备注名", null);

        Assert.IsNull(result);
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
