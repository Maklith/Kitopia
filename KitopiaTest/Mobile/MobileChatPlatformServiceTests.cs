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

}
