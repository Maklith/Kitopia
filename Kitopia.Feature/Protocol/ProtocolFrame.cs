using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Text;

namespace Kitopia.Feature.DeviceCommunication.Protocol;

public static class ProtocolFrame
{
    private static readonly byte[] FrameMagic = Encoding.ASCII.GetBytes("KDC1");

    public const int HeaderLength = 16;

    public static byte[] BuildHeader(int envelopeLength, long payloadLength)
    {
        if (envelopeLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(envelopeLength), "Envelope length must be positive.");
        }

        if (payloadLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadLength), "Payload length cannot be negative.");
        }

        var header = new byte[HeaderLength];
        FrameMagic.CopyTo(header, 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), envelopeLength);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(8, 8), payloadLength);
        return header;
    }

    public static ProtocolFrameHeader ReadHeader(ReadOnlySpan<byte> headerBytes)
    {
        if (headerBytes.Length != HeaderLength)
        {
            throw new InvalidDataException($"Protocol frame header must be {HeaderLength} bytes.");
        }

        if (!headerBytes[..4].SequenceEqual(FrameMagic))
        {
            throw new InvalidDataException("Invalid protocol frame magic.");
        }

        var envelopeLength = BinaryPrimitives.ReadInt32LittleEndian(headerBytes.Slice(4, 4));
        var payloadLength = BinaryPrimitives.ReadInt64LittleEndian(headerBytes.Slice(8, 8));
        if (envelopeLength <= 0)
        {
            throw new InvalidDataException("Protocol frame envelope length must be positive.");
        }

        if (payloadLength < 0)
        {
            throw new InvalidDataException("Protocol frame payload length cannot be negative.");
        }

        return new ProtocolFrameHeader(envelopeLength, payloadLength);
    }

    public static PipeReader CreatePayloadReader(PipeReader inner, long payloadLength)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (payloadLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(payloadLength), "Payload length cannot be negative.");
        }

        return new FramePayloadScopedReader(inner, payloadLength);
    }

    private sealed class FramePayloadScopedReader : PipeReader
    {
        private readonly PipeReader _inner;
        private long _remaining;
        private ReadOnlySequence<byte> _visibleBuffer;

        public FramePayloadScopedReader(PipeReader inner, long length)
        {
            _inner = inner;
            _remaining = length;
        }

        public override void AdvanceTo(SequencePosition consumed)
        {
            AdvanceTo(consumed, consumed);
        }

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            var consumedBytes = _visibleBuffer.Slice(0, consumed).Length;
            _remaining = Math.Max(0, _remaining - consumedBytes);
            _inner.AdvanceTo(consumed, examined);
        }

        public override void CancelPendingRead() => _inner.CancelPendingRead();

        public override void Complete(Exception? exception = null)
        {
        }

        public override ValueTask CompleteAsync(Exception? exception = null)
        {
            return ValueTask.CompletedTask;
        }

        public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (_remaining <= 0)
            {
                _visibleBuffer = ReadOnlySequence<byte>.Empty;
                return new ReadResult(ReadOnlySequence<byte>.Empty, isCanceled: false, isCompleted: true);
            }

            var result = await _inner.ReadAsync(cancellationToken);
            return ScopeReadResult(result);
        }

        public override bool TryRead(out ReadResult result)
        {
            if (_remaining <= 0)
            {
                _visibleBuffer = ReadOnlySequence<byte>.Empty;
                result = new ReadResult(ReadOnlySequence<byte>.Empty, isCanceled: false, isCompleted: true);
                return true;
            }

            if (!_inner.TryRead(out var innerResult))
            {
                result = default;
                return false;
            }

            result = ScopeReadResult(innerResult);
            return true;
        }

        private ReadResult ScopeReadResult(ReadResult result)
        {
            var buffer = result.Buffer;
            if (buffer.Length > _remaining)
            {
                buffer = buffer.Slice(0, _remaining);
            }

            _visibleBuffer = buffer;
            var isCompleted = result.IsCompleted || buffer.Length >= _remaining;
            return new ReadResult(buffer, result.IsCanceled, isCompleted);
        }
    }
}

public readonly record struct ProtocolFrameHeader(int EnvelopeLength, long PayloadLength);