using Core.ViewModel.Pages.device;
using Kitopia.DeviceCommunication.Discovery;

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
    public void DeviceConversationItem_OperatingSystemTag_UsesDiscoveredOperatingSystem()
    {
        var conversation = new DeviceConversationItem("device-1");

        conversation.ApplyDevice(new DiscoveredDevice
        {
            Id = "device-1",
            Name = "Phone",
            OperatingSystem = "Android"
        });

        Assert.IsTrue(conversation.HasOperatingSystem);
        Assert.AreEqual("Android", conversation.OperatingSystemTagText);
    }

    [TestMethod]
    public void CreateFile_DoesNotDisplayKbSize()
    {
        var item = DeviceChatMessageItem.CreateFile("demo.txt", 12_345, isOutgoing: true, DateTimeOffset.Now);

        Assert.IsTrue(item.Text.Contains("KB", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(item.Text, "demo.txt");
    }

    [TestMethod]
    public void IncomingFileOffer_DoesNotDisplayKbSize()
    {
        var transferId = Guid.NewGuid();
        var item = DeviceChatMessageItem.CreateIncomingFileOffer(
            "conversation-1",
            transferId,
            "archive.zip",
            1_048_576,
            DateTimeOffset.Now);

        Assert.IsFalse(item.Text.Contains("KB", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(item.Text, "archive.zip");
        Assert.AreEqual("conversation-1", item.ConversationId);
        Assert.AreEqual(transferId, item.TrackingTransferId);
        Assert.IsTrue(item.IsIncomingFileOffer);
        Assert.IsTrue(item.CanHandleIncomingOffer);
    }

    [TestMethod]
    public void CreateImage_DoesNotDisplayImagePlaceholderText()
    {
        using var source = OpenCvSharp.Mat.Zeros(4, 4, OpenCvSharp.MatType.CV_8UC3);
        OpenCvSharp.Cv2.ImEncode(".png", source, out var bytes);

        var item = DeviceChatMessageItem.CreateImage(bytes, isOutgoing: false, DateTimeOffset.Now);

        Assert.AreEqual(string.Empty, item.Text);
    }

    [TestMethod]
    public void FileChatMessageItem_OutgoingPending_ShowsSendingRequest()
    {
        var item = new FileChatMessageItem("test.zip", 1024, isOutgoing: true, DateTimeOffset.Now);
        Assert.IsTrue(item.IsPending);
        Assert.AreEqual("正在发送请求...", item.StateText);
    }

    [TestMethod]
    public void FileChatMessageItem_OfferDelivered_ShowsWaitingForAccept()
    {
        var item = new FileChatMessageItem("test.zip", 1024, isOutgoing: true, DateTimeOffset.Now);
        item.IsOfferDelivered = true;
        item.IsWaitingForAccept = true;
        Assert.AreEqual("请求已送达，等待对方接受...", item.StateText);
    }

    [TestMethod]
    public void FileChatMessageItem_IncomingFileOffer_ShowsWaiting()
    {
        var item = new FileChatMessageItem("test.zip", 1024, isOutgoing: false, DateTimeOffset.Now);
        item.IsIncomingFileOffer = true;
        item.TrackingTransferId = Guid.NewGuid();
        Assert.AreEqual("等待接收", item.StateText);
        Assert.IsTrue(item.CanHandleIncomingOffer);
    }

    [TestMethod]
    public void FileChatMessageItem_ReceivingOutgoing_ShowsSpeedAndPercent()
    {
        var item = new FileChatMessageItem("test.zip", 10 * 1024 * 1024, isOutgoing: true, DateTimeOffset.Now);
        item.IsReceiving = true;
        item.ReceiveProgress = 0.45;
        item.TransferSpeedBytesPerSecond = 2.5 * 1024 * 1024;
        var state = item.StateText;
        StringAssert.Contains(state, "发送中");
        StringAssert.Contains(state, "2.5 MB/s");
        StringAssert.Contains(state, "45%");
    }

    [TestMethod]
    public void FileChatMessageItem_ReceivingIncoming_ShowsReceiving()
    {
        var item = new FileChatMessageItem("test.zip", 10 * 1024 * 1024, isOutgoing: false, DateTimeOffset.Now);
        item.IsReceiving = true;
        item.ReceiveProgress = 0.5;
        var state = item.StateText;
        StringAssert.Contains(state, "接收中");
        StringAssert.Contains(state, "50%");
    }

    [TestMethod]
    public void FileChatMessageItem_Failed_ShowsFailed()
    {
        var item = new FileChatMessageItem("test.zip", 1024, isOutgoing: true, DateTimeOffset.Now);
        item.IsFailed = true;
        Assert.AreEqual("失败", item.StateText);
    }

    [TestMethod]
    public void FileChatMessageItem_Completed_ShowsDone()
    {
        var item = new FileChatMessageItem("test.zip", 1024, isOutgoing: true, DateTimeOffset.Now);
        item.IsPending = false;
        item.IsReceiving = false;
        item.IsFailed = false;
        Assert.AreEqual("已完成", item.StateText);
    }

    [TestMethod]
    public void FileChatMessageItem_FormatFileSizeLabel_Valid()
    {
        Assert.AreEqual("1.00 KB", FileChatMessageItem.FormatFileSizeLabel(1024));
        Assert.AreEqual("1.00 MB", FileChatMessageItem.FormatFileSizeLabel(1024 * 1024));
        Assert.AreEqual("500 字节", FileChatMessageItem.FormatFileSizeLabel(500));
    }

    [TestMethod]
    public void FileChatMessageItem_HasLocalFile_ReturnsTrueForExisting()
    {
        var tempFile = System.IO.Path.GetTempFileName();
        try
        {
            var item = new FileChatMessageItem("test.tmp", 0, isOutgoing: false, DateTimeOffset.Now)
            {
                LocalFilePath = tempFile
            };
            Assert.IsTrue(item.HasLocalFile);
        }
        finally
        {
            System.IO.File.Delete(tempFile);
        }
    }
}
