using Core.Services.DeviceCommunication.Protocol;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class FrameProtocolTests
{
    [TestMethod]
    public void TryReadFrameHeader_ReturnsFalse_WhenBufferTooShort()
    {
        var source = new byte[FrameProtocol.HeaderLength - 1];
        var ok = FrameProtocol.TryReadFrameHeader(source, out _, out _);
        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void TryReadFrameHeader_ReturnsFalse_WhenPayloadLengthNegative()
    {
        var bytes = new byte[FrameProtocol.HeaderLength];
        bytes[0] = FrameProtocol.CurrentVersion;
        bytes[1] = 1;
        bytes[2] = 0;
        BitConverter.GetBytes(-1).CopyTo(bytes, 19);

        var ok = FrameProtocol.TryReadFrameHeader(bytes, out _, out _);
        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void WriteAndReadFrameHeader_RoundTrips()
    {
        var expected = new FrameHeader(
            Version: FrameProtocol.CurrentVersion,
            FrameType: 2,
            Flags: 1,
            ChannelId: Guid.NewGuid(),
            PayloadLength: 128);

        var bytes = new byte[FrameProtocol.HeaderLength];
        FrameProtocol.WriteFrameHeader(bytes, expected);

        var ok = FrameProtocol.TryReadFrameHeader(bytes, out var actual, out var consumed);
        Assert.IsTrue(ok);
        Assert.AreEqual(FrameProtocol.HeaderLength, consumed);
        Assert.AreEqual(expected, actual);
    }
}
