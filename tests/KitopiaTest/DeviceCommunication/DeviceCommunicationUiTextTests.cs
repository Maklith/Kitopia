using Kitopia.Feature.Avalonia.DeviceCommunication.ViewModels;
using Kitopia.Feature.DeviceCommunication.Discovery;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public class DeviceCommunicationUiTextTests
{
    [TestMethod]
    [DataRow(null, false)]
    [DataRow("0.1", true)]
    [DataRow("0.2", false)]
    [DataRow("0.3", false)]
    public void DeviceConversationItem_ProtocolVersion_OnlyOlderKnownVersionsShowWarning(string? version, bool expected)
    {
        var conversation = new DeviceConversationItem("peer");
        conversation.ApplyDevice(new DiscoveredDevice
        {
            Id = "peer", DiscoveryProtocolVersion = version is null ? null : Version.Parse(version)
        });

        Assert.AreEqual(expected, conversation.IsLegacyDevice);
        if (expected)
        {
            StringAssert.Contains(conversation.LegacyDeviceDetails, version!);
            StringAssert.Contains(conversation.LegacyDeviceDetails, DeviceDiscoveryService.CurrentProtocolVersion.ToString());
        }
    }

    [TestMethod]
    public void CreateImage_DoesNotDisplayImagePlaceholderText()
    {
        using var source = new OpenCvSharp.Mat(4, 4, OpenCvSharp.MatType.CV_8UC3, OpenCvSharp.Scalar.Black);
        OpenCvSharp.Cv2.ImEncode(".png", source, out var bytes);

        var item = DeviceChatMessageItem.CreateImage(bytes, isOutgoing: false, DateTimeOffset.Now);

        Assert.AreEqual(string.Empty, item.Text);
    }
}
