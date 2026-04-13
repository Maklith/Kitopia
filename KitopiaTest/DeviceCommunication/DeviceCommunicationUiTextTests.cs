using Core.Services.DeviceCommunication;
using Core.ViewModel.Pages.device;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public class DeviceCommunicationUiTextTests
{
    [TestMethod]
    public void DeviceConversationItem_StatusText_UsesChineseOnlineOffline()
    {
        var conversation = new DeviceConversationItem("device-1");

        conversation.IsOnline = true;
        Assert.AreEqual("在线", conversation.StatusText);

        conversation.IsOnline = false;
        Assert.AreEqual("离线", conversation.StatusText);
    }

    [TestMethod]
    public void CreateFile_DoesNotDisplayKbSize()
    {
        var item = DeviceChatMessageItem.CreateFile("demo.txt", 12_345, isOutgoing: true, DateTimeOffset.Now);

        Assert.IsFalse(item.Text.Contains("KB", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(item.Text, "demo.txt");
    }

    [TestMethod]
    public void IncomingFileOffer_DoesNotDisplayKbSize()
    {
        var item = new IncomingFileOfferChatMessageItem(
            "conversation-1",
            Guid.NewGuid(),
            "archive.zip",
            1_048_576,
            LocalDataTransportProtocol.Tcp,
            10086,
            DateTimeOffset.Now);

        Assert.IsFalse(item.Text.Contains("KB", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(item.Text, "archive.zip");
    }
}
