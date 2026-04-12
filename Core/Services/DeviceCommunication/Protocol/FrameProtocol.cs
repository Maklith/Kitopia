using System.Buffers.Binary;

namespace Core.Services.DeviceCommunication.Protocol;

public static class FrameProtocol
{
    public const int HeaderLength = 23;
    public const byte CurrentVersion = 1;

    public static bool TryReadFrameHeader(
        ReadOnlySpan<byte> source,
        out FrameHeader header,
        out int consumed)
    {
        header = default;
        consumed = 0;
        if (source.Length < HeaderLength)
        {
            return false;
        }

        var version = source[0];
        var frameType = source[1];
        var flags = source[2];
        var channelBytes = source.Slice(3, 16);
        var channelId = new Guid(channelBytes);
        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(source.Slice(19, 4));
        if (payloadLength < 0)
        {
            return false;
        }

        header = new FrameHeader(version, frameType, flags, channelId, payloadLength);
        consumed = HeaderLength;
        return true;
    }

    public static void WriteFrameHeader(Span<byte> destination, FrameHeader header)
    {
        if (destination.Length < HeaderLength)
        {
            throw new ArgumentException("Destination buffer too small.", nameof(destination));
        }

        destination[0] = header.Version;
        destination[1] = header.FrameType;
        destination[2] = header.Flags;
        header.ChannelId.TryWriteBytes(destination.Slice(3, 16));
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(19, 4), header.PayloadLength);
    }
}
