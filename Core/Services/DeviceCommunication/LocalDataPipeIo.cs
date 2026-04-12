using System.Buffers;
using System.IO.Pipelines;

namespace Core.Services.DeviceCommunication;

internal static class LocalDataPipeIo
{
    public static async Task<byte[]?> ReadExactlyOrEndAsync(
        PipeReader payloadReader,
        int byteCount,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[byteCount];
        var filled = 0;
        while (filled < byteCount)
        {
            var readResult = await payloadReader.ReadAsync(cancellationToken);
            var sequence = readResult.Buffer;
            if (sequence.Length == 0)
            {
                payloadReader.AdvanceTo(sequence.End);
                if (readResult.IsCompleted)
                {
                    if (filled == 0)
                    {
                        return null;
                    }

                    throw new EndOfStreamException("Unexpected end of stream while reading fixed-length data.");
                }

                continue;
            }

            var toCopy = (int)Math.Min(sequence.Length, byteCount - filled);
            CopySequenceToSpan(sequence.Slice(0, toCopy), buffer.AsSpan(filled, toCopy));
            filled += toCopy;
            payloadReader.AdvanceTo(sequence.GetPosition(toCopy), sequence.End);
        }

        return buffer;
    }

    public static async Task<byte[]> ReadUpToAsync(
        PipeReader payloadReader,
        int maxByteCount,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[maxByteCount];
        var filled = 0;
        while (filled < maxByteCount)
        {
            var readResult = await payloadReader.ReadAsync(cancellationToken);
            var sequence = readResult.Buffer;
            if (sequence.Length == 0)
            {
                payloadReader.AdvanceTo(sequence.End);
                if (readResult.IsCompleted)
                {
                    break;
                }

                continue;
            }

            var toCopy = (int)Math.Min(sequence.Length, maxByteCount - filled);
            CopySequenceToSpan(sequence.Slice(0, toCopy), buffer.AsSpan(filled, toCopy));
            filled += toCopy;
            payloadReader.AdvanceTo(sequence.GetPosition(toCopy), sequence.End);
            if (readResult.IsCompleted)
            {
                break;
            }
        }

        return filled == buffer.Length ? buffer : buffer[..filled];
    }

    public static async Task<byte[]> ReadExactlyAsync(
        PipeReader payloadReader,
        int byteCount,
        CancellationToken cancellationToken)
    {
        var payload = await ReadExactlyOrEndAsync(payloadReader, byteCount, cancellationToken);
        if (payload is null)
        {
            throw new EndOfStreamException("Unexpected end of stream.");
        }

        return payload;
    }

    public static async Task CopyExactlyToStreamAsync(
        PipeReader payloadReader,
        Stream stream,
        int byteCount,
        CancellationToken cancellationToken)
    {
        var remaining = byteCount;
        while (remaining > 0)
        {
            var readResult = await payloadReader.ReadAsync(cancellationToken);
            var sequence = readResult.Buffer;
            if (sequence.Length == 0)
            {
                payloadReader.AdvanceTo(sequence.End);
                if (readResult.IsCompleted)
                {
                    throw new EndOfStreamException("Unexpected end of stream while reading frame payload.");
                }

                continue;
            }

            var toCopy = (int)Math.Min(sequence.Length, remaining);
            foreach (var segment in sequence.Slice(0, toCopy))
            {
                await stream.WriteAsync(segment, cancellationToken);
            }

            remaining -= toCopy;
            payloadReader.AdvanceTo(sequence.GetPosition(toCopy), sequence.End);
        }
    }

    public static async Task DrainExactlyAsync(
        PipeReader payloadReader,
        int byteCount,
        CancellationToken cancellationToken)
    {
        var remaining = byteCount;
        while (remaining > 0)
        {
            var readResult = await payloadReader.ReadAsync(cancellationToken);
            var sequence = readResult.Buffer;
            if (sequence.Length == 0)
            {
                payloadReader.AdvanceTo(sequence.End);
                if (readResult.IsCompleted)
                {
                    throw new EndOfStreamException("Unexpected end of stream while draining payload.");
                }

                continue;
            }

            var consumed = (int)Math.Min(sequence.Length, remaining);
            remaining -= consumed;
            payloadReader.AdvanceTo(sequence.GetPosition(consumed), sequence.End);
        }
    }

    private static void CopySequenceToSpan(in ReadOnlySequence<byte> source, Span<byte> destination)
    {
        var written = 0;
        foreach (var segment in source)
        {
            segment.Span.CopyTo(destination.Slice(written));
            written += segment.Length;
        }
    }
}
