using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using Core.Services.DeviceCommunication.Routing;

namespace Core.Services.DeviceCommunication.Protocol;

public sealed class ProtocolSender
{
    private static readonly byte[] FrameMagic = "KDC1"u8.ToArray();
    private static readonly StreamPipeReaderOptions PayloadPipeReaderOptions = new(
        bufferSize: 256 * 1024,
        minimumReadSize: 64 * 1024,
        leaveOpen: true);
    private readonly ILocalDataListener _listener;

    public ProtocolSender(ILocalDataListener listener)
    {
        _listener = listener;
    }

    public Task SendAsync(
        MessageContext context,
        DataEnvelope envelope,
        Stream? payloadStream = null,
        Func<long, long, ValueTask>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        payloadStream ??= Stream.Null;
        if (!payloadStream.CanRead)
        {
            throw new InvalidOperationException("Payload stream must be readable.");
        }

        if (!payloadStream.CanSeek)
        {
            throw new InvalidOperationException("Payload stream must be seekable.");
        }

        var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var payloadLength = payloadStream.Length - payloadStream.Position;
        if (payloadLength < 0)
        {
            throw new InvalidOperationException("Invalid payload stream length.");
        }

        var frameHeader = BuildFrameHeader(envelopeBytes.Length, payloadLength);
        var progressStream = new ProgressReportingReadStream(payloadStream, payloadLength, progressCallback);
        return SendPrefixAndPayloadAsync(context, frameHeader, envelopeBytes, progressStream, cancellationToken);
    }

    private static byte[] BuildFrameHeader(int envelopeLength, long payloadLength)
    {
        var header = new byte[16];
        FrameMagic.CopyTo(header, 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), envelopeLength);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(8, 8), payloadLength);
        return header;
    }

    private Task SendPrefixAndPayloadAsync(
        MessageContext context,
        byte[] frame,
        byte[] envelope,
        Stream payloadStream,
        CancellationToken cancellationToken)
    {
        var compositeStream = new PrefixConcatStream(frame, envelope, payloadStream);
        var reader = PipeReader.Create(compositeStream, PayloadPipeReaderOptions);

        return SendReaderAsync(context, reader, cancellationToken);
    }

    private async Task SendReaderAsync(
        MessageContext context,
        PipeReader reader,
        CancellationToken cancellationToken)
    {
        Exception? sendError = null;
        try
        {
            await _listener.SendAsync(
                context.Protocol,
                reader,
                context.RemoteEndPoint,
                context.RemoteIdentityPublicKey,
                cancellationToken);
        }
        catch (Exception ex)
        {
            sendError = ex;
            throw;
        }
        finally
        {
            await reader.CompleteAsync(sendError);
        }
    }

    private sealed class PrefixConcatStream : Stream
    {
        private readonly byte[] _frame;
        private readonly byte[] _envelope;
        private readonly Stream _payload;
        private int _frameOffset;
        private int _envelopeOffset;

        public PrefixConcatStream(byte[] frame, byte[] envelope, Stream payload)
        {
            _frame = frame;
            _envelope = envelope;
            _payload = payload;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_frameOffset < _frame.Length)
            {
                var copy = Math.Min(count, _frame.Length - _frameOffset);
                _frame.AsSpan(_frameOffset, copy).CopyTo(buffer.AsSpan(offset, copy));
                _frameOffset += copy;
                return copy;
            }

            if (_envelopeOffset < _envelope.Length)
            {
                var copy = Math.Min(count, _envelope.Length - _envelopeOffset);
                _envelope.AsSpan(_envelopeOffset, copy).CopyTo(buffer.AsSpan(offset, copy));
                _envelopeOffset += copy;
                return copy;
            }

            return _payload.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_frameOffset < _frame.Length)
            {
                var copy = Math.Min(buffer.Length, _frame.Length - _frameOffset);
                _frame.AsMemory(_frameOffset, copy).CopyTo(buffer);
                _frameOffset += copy;
                return ValueTask.FromResult(copy);
            }

            if (_envelopeOffset < _envelope.Length)
            {
                var copy = Math.Min(buffer.Length, _envelope.Length - _envelopeOffset);
                _envelope.AsMemory(_envelopeOffset, copy).CopyTo(buffer);
                _envelopeOffset += copy;
                return ValueTask.FromResult(copy);
            }

            return _payload.ReadAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ProgressReportingReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _total;
        private readonly Func<long, long, ValueTask>? _callback;
        private long _sent;
        private long _lastReportedBytes;
        private int _lastReportedPercent = -1;
        private const int ProgressStepBytes = 1024 * 1024;

        public ProgressReportingReadStream(Stream inner, long total, Func<long, long, ValueTask>? callback)
        {
            _inner = inner;
            _total = total;
            _callback = callback;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            Report(read).GetAwaiter().GetResult();
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken);
            await Report(read);
            return read;
        }

        private ValueTask Report(int read)
        {
            if (read <= 0 || _callback is null)
            {
                return ValueTask.CompletedTask;
            }

            _sent += read;
            var percent = _total > 0
                ? (int)Math.Clamp((_sent * 100L) / _total, 0, 100)
                : 100;
            var shouldReport = _sent == _total ||
                               _sent - _lastReportedBytes >= ProgressStepBytes ||
                               percent > _lastReportedPercent;
            if (!shouldReport)
            {
                return ValueTask.CompletedTask;
            }

            _lastReportedBytes = _sent;
            _lastReportedPercent = percent;
            return _callback(_sent, _total);
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
