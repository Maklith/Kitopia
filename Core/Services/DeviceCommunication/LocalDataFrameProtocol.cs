using System.Buffers.Binary;
using System.IO.Pipelines;

namespace Core.Services.DeviceCommunication;

internal static class LocalDataFrameProtocol
{
    private const int MultiplexMagic = 0x4B544D58; // KTMX
    private const byte MultiplexVersion = 1;

    public const int MultiplexHeaderLength = 5;
    public const int FrameHeaderLength = 21;

    public static bool IsMultiplexStream(ReadOnlySpan<byte> header)
    {
        if (header.Length < MultiplexHeaderLength)
        {
            return false;
        }

        return BinaryPrimitives.ReadInt32BigEndian(header[..4]) == MultiplexMagic &&
               header[4] == MultiplexVersion;
    }

    public static void WriteMultiplexHeader(PipeWriter writer)
    {
        var span = writer.GetSpan(MultiplexHeaderLength);
        WriteMultiplexHeader(span[..MultiplexHeaderLength]);
        writer.Advance(MultiplexHeaderLength);
    }

    public static void WriteMultiplexHeader(Span<byte> span)
    {
        BinaryPrimitives.WriteInt32BigEndian(span[..4], MultiplexMagic);
        span[4] = MultiplexVersion;
    }

    public static void WriteFrameHeader(
        PipeWriter writer,
        byte frameType,
        Guid channelId,
        int payloadLength)
    {
        var span = writer.GetSpan(FrameHeaderLength);
        WriteFrameHeader(span[..FrameHeaderLength], frameType, channelId, payloadLength);
        writer.Advance(FrameHeaderLength);
    }

    public static void WriteFrameHeader(
        Span<byte> span,
        byte frameType,
        Guid channelId,
        int payloadLength)
    {
        span[0] = frameType;
        channelId.TryWriteBytes(span.Slice(1, 16));
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(17, 4), payloadLength);
    }

    public static void ParseFrameHeader(
        ReadOnlySpan<byte> header,
        out byte frameType,
        out Guid frameChannelId,
        out int payloadLength)
    {
        if (header.Length < FrameHeaderLength)
        {
            throw new ArgumentException("Invalid frame header length.", nameof(header));
        }

        frameType = header[0];
        frameChannelId = new Guid(header.Slice(1, 16));
        payloadLength = BinaryPrimitives.ReadInt32BigEndian(header.Slice(17, 4));
    }
}
