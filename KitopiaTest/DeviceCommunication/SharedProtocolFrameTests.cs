using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using Kitopia.Feature.DeviceCommunication.Protocol;
using Kitopia.Feature.DeviceCommunication.Routing;
using Kitopia.Feature.DeviceCommunication.Transport;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class SharedProtocolFrameTests
{
    [TestMethod]
    public void BuildHeader_ThenReadHeader_RoundTrips()
    {
        var headerBytes = ProtocolFrame.BuildHeader(128, 4096);

        var header = ProtocolFrame.ReadHeader(headerBytes);

        Assert.AreEqual(ProtocolFrame.HeaderLength, headerBytes.Length);
        Assert.AreEqual("KDC1", Encoding.ASCII.GetString(headerBytes, 0, 4));
        Assert.AreEqual(128, header.EnvelopeLength);
        Assert.AreEqual(4096, header.PayloadLength);
    }

    [TestMethod]
    public void BuildHeader_MatchesLegacyCoreWireFormat()
    {
        var headerBytes = ProtocolFrame.BuildHeader(0x01020304, 0x0102030405060708);

        CollectionAssert.AreEqual(
            new byte[]
            {
                (byte)'K', (byte)'D', (byte)'C', (byte)'1',
                0x04, 0x03, 0x02, 0x01,
                0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01
            },
            headerBytes);
    }

    [TestMethod]
    public void ReadHeader_Throws_WhenMagicInvalid()
    {
        var headerBytes = ProtocolFrame.BuildHeader(128, 4096);
        Encoding.ASCII.GetBytes("BAD!").CopyTo(headerBytes, 0);

        Assert.ThrowsExactly<InvalidDataException>(() => ProtocolFrame.ReadHeader(headerBytes));
    }

    [TestMethod]
    public void ReadHeader_Throws_WhenEnvelopeLengthZero()
    {
        var headerBytes = ProtocolFrame.BuildHeader(128, 4096);
        BinaryPrimitives.WriteInt32LittleEndian(headerBytes.AsSpan(4, 4), 0);

        Assert.ThrowsExactly<InvalidDataException>(() => ProtocolFrame.ReadHeader(headerBytes));
    }

    [TestMethod]
    public void ReadHeader_Throws_WhenPayloadLengthNegative()
    {
        var headerBytes = ProtocolFrame.BuildHeader(128, 4096);
        BinaryPrimitives.WriteInt64LittleEndian(headerBytes.AsSpan(8, 8), -1);

        Assert.ThrowsExactly<InvalidDataException>(() => ProtocolFrame.ReadHeader(headerBytes));
    }
    [TestMethod]
    public async Task WriteAndReadFrame_RoundTripsEnvelopeAndPayload()
    {
        var envelope = new DataEnvelope
        {
            Route = "chat",
            Command = "image.direct",
            StreamType = DataStreamType.Image,
            ChannelId = Guid.NewGuid(),
            Sequence = 42,
            ContentType = "image/png",
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["conversationId"] = "peer-1",
                ["sizeBytes"] = "3"
            }
        };
        var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var payloadBytes = new byte[] { 10, 20, 30 };
        var frameBytes = ProtocolFrame.BuildHeader(envelopeBytes.Length, payloadBytes.Length)
            .Concat(envelopeBytes)
            .Concat(payloadBytes)
            .ToArray();

        var reader = PipeReader.Create(new MemoryStream(frameBytes));
        var headerBytes = await LocalDataPipeIo.ReadExactlyAsync(
            reader,
            ProtocolFrame.HeaderLength,
            CancellationToken.None);
        var header = ProtocolFrame.ReadHeader(headerBytes);
        var actualEnvelopeBytes = await LocalDataPipeIo.ReadExactlyAsync(
            reader,
            header.EnvelopeLength,
            CancellationToken.None);
        var actualPayloadBytes = await LocalDataPipeIo.ReadExactlyAsync(
            ProtocolFrame.CreatePayloadReader(reader, header.PayloadLength),
            (int)header.PayloadLength,
            CancellationToken.None);

        var actualEnvelope = JsonSerializer.Deserialize<DataEnvelope>(actualEnvelopeBytes);
        Assert.IsNotNull(actualEnvelope);
        Assert.AreEqual(envelope.Route, actualEnvelope.Route);
        Assert.AreEqual(envelope.Command, actualEnvelope.Command);
        Assert.AreEqual(envelope.StreamType, actualEnvelope.StreamType);
        Assert.AreEqual(envelope.ChannelId, actualEnvelope.ChannelId);
        Assert.AreEqual(envelope.Sequence, actualEnvelope.Sequence);
        Assert.AreEqual(envelope.ContentType, actualEnvelope.ContentType);
        Assert.AreEqual("peer-1", actualEnvelope.Metadata?["conversationId"]);
        CollectionAssert.AreEqual(payloadBytes, actualPayloadBytes);
    }

    [TestMethod]
    public async Task CreatePayloadReader_DoesNotExposeTrailingFrameBytes()
    {
        var declaredPayload = new byte[] { 1, 2, 3 };
        var trailingBytes = new byte[] { 4, 5, 6 };
        var reader = PipeReader.Create(new MemoryStream(declaredPayload.Concat(trailingBytes).ToArray()));
        var scopedReader = ProtocolFrame.CreatePayloadReader(reader, declaredPayload.Length);

        var scopedPayload = await LocalDataPipeIo.ReadExactlyAsync(
            scopedReader,
            declaredPayload.Length,
            CancellationToken.None);

        CollectionAssert.AreEqual(declaredPayload, scopedPayload);

        var trailing = await LocalDataPipeIo.ReadExactlyAsync(reader, trailingBytes.Length, CancellationToken.None);
        CollectionAssert.AreEqual(trailingBytes, trailing);
    }
}
