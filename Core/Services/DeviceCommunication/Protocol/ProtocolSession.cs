using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Text.Json;
using Core.Services.DeviceCommunication.Routing;

namespace Core.Services.DeviceCommunication.Protocol;

public sealed class ProtocolSession
{
    private static readonly byte[] FrameMagic = Encoding.ASCII.GetBytes("KDC1");
    private readonly DeviceMessageDispatcher _dispatcher;

    public ProtocolSession(DeviceMessageDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async ValueTask HandleAsync(
        LocalDataTransportProtocol protocol,
        IPEndPoint remoteEndPoint,
        PipeReader payloadReader,
        CancellationToken cancellationToken = default)
    {
        var frameHeader = await LocalDataPipeIo.ReadExactlyOrEndAsync(payloadReader, 16, cancellationToken);
        if (frameHeader is null)
        {
            return;
        }

        if (!frameHeader.AsSpan(0, 4).SequenceEqual(FrameMagic))
        {
            throw new InvalidOperationException("Invalid protocol frame magic.");
        }

        var envelopeLength = BinaryPrimitives.ReadInt32LittleEndian(frameHeader.AsSpan(4, 4));
        var payloadLength = BinaryPrimitives.ReadInt64LittleEndian(frameHeader.AsSpan(8, 8));
        if (envelopeLength <= 0 || payloadLength < 0)
        {
            throw new InvalidOperationException("Invalid frame lengths.");
        }

        var envelopeBytes = await LocalDataPipeIo.ReadExactlyAsync(payloadReader, envelopeLength, cancellationToken);
        var envelope = JsonSerializer.Deserialize<DataEnvelope>(envelopeBytes);
        if (envelope is null || string.IsNullOrWhiteSpace(envelope.Route))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(envelope.Metadata?.TryGetValue("senderId", out var senderId) == true ? senderId : null))
        {
            envelope = new DataEnvelope
            {
                Route = envelope.Route,
                Command = envelope.Command,
                StreamType = envelope.StreamType,
                ChannelId = envelope.ChannelId,
                Sequence = envelope.Sequence,
                ContentType = envelope.ContentType,
                Metadata = MergeMetadata(envelope.Metadata, remoteEndPoint.Address.ToString())
            };
        }

        var scopedPayloadReader = new FramePayloadScopedReader(payloadReader, payloadLength);
        var context = new MessageContext(protocol, remoteEndPoint, string.Empty);
        await _dispatcher.DispatchAsync(context, envelope, scopedPayloadReader, cancellationToken);
    }

    private static IReadOnlyDictionary<string, string?> MergeMetadata(
        IReadOnlyDictionary<string, string?>? metadata,
        string senderIdFallback)
    {
        var merged = metadata is null
            ? new Dictionary<string, string?>(StringComparer.Ordinal)
            : new Dictionary<string, string?>(metadata, StringComparer.Ordinal);
        if (!merged.ContainsKey("senderId"))
        {
            merged["senderId"] = senderIdFallback;
        }

        return merged;
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
            _remaining -= consumedBytes;
            if (_remaining < 0)
            {
                _remaining = 0;
            }

            _inner.AdvanceTo(consumed, examined);
        }

        public override void CancelPendingRead() => _inner.CancelPendingRead();

        public override void Complete(Exception? exception = null) { }

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
            var buffer = result.Buffer;
            if (buffer.Length > _remaining)
            {
                buffer = buffer.Slice(0, _remaining);
            }

            _visibleBuffer = buffer;
            var isCompleted = result.IsCompleted || buffer.Length >= _remaining;
            return new ReadResult(buffer, result.IsCanceled, isCompleted);
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

            var buffer = innerResult.Buffer;
            if (buffer.Length > _remaining)
            {
                buffer = buffer.Slice(0, _remaining);
            }

            _visibleBuffer = buffer;
            var isCompleted = innerResult.IsCompleted || buffer.Length >= _remaining;
            result = new ReadResult(buffer, innerResult.IsCanceled, isCompleted);
            return true;
        }
    }
}
