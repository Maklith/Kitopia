using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Core.Services.DeviceCommunication;

public sealed partial class LocalDataStreamControl
{
    private async Task CleanupStateAsync()
    {
        var nowUtc = DateTime.UtcNow;
        bool shouldCleanup;
        lock (_channelSync)
        {
            shouldCleanup = nowUtc - _lastCleanupUtc >= ChannelCleanupInterval;
            if (shouldCleanup)
            {
                _lastCleanupUtc = nowUtc;
            }
        }

        if (!shouldCleanup)
        {
            return;
        }

        lock (_channelSync)
        {
            List<ChannelContextKey>? expiredKeys = null;
            foreach (var pair in _channelRoutes)
            {
                if (nowUtc - pair.Value.UpdatedUtc <= ChannelRouteTtl)
                {
                    continue;
                }

                expiredKeys ??= [];
                expiredKeys.Add(pair.Key);
            }

            if (expiredKeys is not null)
            {
                foreach (var key in expiredKeys)
                {
                    _channelRoutes.Remove(key);
                }
            }
        }

        var handlerSet = new HashSet<IBusRouteHandler>(_routeHandlers.Values);
        foreach (var handler in handlerSet)
        {
            await handler.CleanupAsync(nowUtc);
        }
    }

    private IBusRouteHandler ResolveRouteHandler(string route)
    {
        if (_routeHandlers.TryGetValue(route, out var handler))
        {
            return handler;
        }

        return _routeHandlers[RouteCommand];
    }

    private void SetChannelRoute(BusRouteContext context, Guid channelId, string route)
    {
        var key = CreateChannelContextKey(context, channelId);
        lock (_channelSync)
        {
            _channelRoutes[key] = new ChannelRouteBinding(route, DateTime.UtcNow);
        }
    }

    private bool TryGetChannelRoute(BusRouteContext context, Guid channelId, out string route)
    {
        var key = CreateChannelContextKey(context, channelId);
        lock (_channelSync)
        {
            if (_channelRoutes.TryGetValue(key, out var binding))
            {
                route = binding.Route;
                return true;
            }
        }

        route = string.Empty;
        return false;
    }

    private void TouchChannelRoute(BusRouteContext context, Guid channelId)
    {
        var key = CreateChannelContextKey(context, channelId);
        lock (_channelSync)
        {
            if (_channelRoutes.TryGetValue(key, out var binding))
            {
                binding.UpdatedUtc = DateTime.UtcNow;
            }
        }
    }

    private void RemoveChannelRoute(BusRouteContext context, Guid channelId)
    {
        var key = CreateChannelContextKey(context, channelId);
        lock (_channelSync)
        {
            _channelRoutes.Remove(key);
        }
    }

    private async Task SendFileByStreamFramesAsync(
        LocalDataSendContext sendContext,
        Stream stream,
        string? fileName,
        int framePayloadSize,
        CancellationToken cancellationToken)
    {
        var channelId = Guid.NewGuid();
        var beginEnvelope = CreateFileEnvelope(channelId, FileCommandBegin, fileName, stream);
        var endEnvelope = CreateFileEnvelope(channelId, FileCommandEnd, fileName, null);
        var cancelEnvelope = CreateFileEnvelope(channelId, FileCommandCancel, fileName, null);
        var pipe = new Pipe();

        async Task ProduceAsync()
        {
            Exception? producerError = null;
            var sendSucceeded = false;
            var headerWritten = false;
            var rented = ArrayPool<byte>.Shared.Rent(framePayloadSize);
            try
            {
                WriteMultiplexHeader(pipe.Writer);
                headerWritten = true;
                await WriteEnvelopeFrameAsync(pipe.Writer, channelId, beginEnvelope, cancellationToken);

                while (true)
                {
                    var read = await stream.ReadAsync(rented.AsMemory(0, framePayloadSize), cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    await WritePayloadFrameAsync(pipe.Writer, channelId, rented.AsMemory(0, read), cancellationToken);
                }

                await WriteEnvelopeFrameAsync(pipe.Writer, channelId, endEnvelope, cancellationToken);
                sendSucceeded = true;
            }
            catch (Exception ex)
            {
                producerError = ex;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented, clearArray: false);
                if (!sendSucceeded && headerWritten)
                {
                    try
                    {
                        await WriteEnvelopeFrameAsync(pipe.Writer, channelId, cancelEnvelope, cancellationToken);
                    }
                    catch
                    {
                    }
                }

                await pipe.Writer.CompleteAsync(producerError);
            }
        }

        var producerTask = ProduceAsync();
        Exception? consumerError = null;
        try
        {
            await sendContext.Listener.SendAsync(
                sendContext.Protocol,
                pipe.Reader,
                sendContext.RemoteEndPoint,
                sendContext.RemoteIdentityPublicKey,
                cancellationToken);
        }
        catch (Exception ex)
        {
            consumerError = ex;
            throw;
        }
        finally
        {
            await pipe.Reader.CompleteAsync(consumerError);
        }

        await producerTask;
    }

    private static Task SendEnvelopePacketAsync(
        LocalDataSendContext sendContext,
        Guid channelId,
        BusEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ValidateSendContext(sendContext);
        envelope.ChannelId ??= channelId == Guid.Empty ? null : channelId.ToString("D");
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope);
        return SendFramePacketAsync(sendContext, BusFrameType.Envelope, channelId, payload, cancellationToken);
    }

    private static Task SendPayloadPacketAsync(
        LocalDataSendContext sendContext,
        Guid channelId,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        ValidateSendContext(sendContext);
        return SendFramePacketAsync(sendContext, BusFrameType.Payload, channelId, payload, cancellationToken);
    }

    private static async Task SendFramePacketAsync(
        LocalDataSendContext sendContext,
        BusFrameType frameType,
        Guid channelId,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var packetLength = MultiplexHeaderLength + FrameHeaderLength + payload.Length;
        var rented = ArrayPool<byte>.Shared.Rent(packetLength);
        try
        {
            var span = rented.AsSpan(0, packetLength);
            WriteMultiplexHeader(span[..MultiplexHeaderLength]);
            WriteFrameHeader(span.Slice(MultiplexHeaderLength, FrameHeaderLength), frameType, channelId, payload.Length);
            payload.Span.CopyTo(span[(MultiplexHeaderLength + FrameHeaderLength)..]);

            await sendContext.Listener.SendAsync(
                sendContext.Protocol,
                rented.AsMemory(0, packetLength),
                sendContext.RemoteEndPoint,
                sendContext.RemoteIdentityPublicKey,
                cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: false);
        }
    }

    private static async Task WriteEnvelopeFrameAsync(
        PipeWriter writer,
        Guid channelId,
        BusEnvelope envelope,
        CancellationToken cancellationToken)
    {
        envelope.ChannelId ??= channelId == Guid.Empty ? null : channelId.ToString("D");
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope);
        WriteFrameHeader(writer, BusFrameType.Envelope, channelId, payload.Length);
        writer.Write(payload);
        await EnsureFlushedAsync(writer, cancellationToken);
    }

    private static async Task WritePayloadFrameAsync(
        PipeWriter writer,
        Guid channelId,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        WriteFrameHeader(writer, BusFrameType.Payload, channelId, payload.Length);
        writer.Write(payload.Span);
        await EnsureFlushedAsync(writer, cancellationToken);
    }

    private static async Task EnsureFlushedAsync(PipeWriter writer, CancellationToken cancellationToken)
    {
        var flushResult = await writer.FlushAsync(cancellationToken);
        if (flushResult.IsCanceled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (flushResult.IsCompleted)
        {
            throw new IOException("Pipe writer completed unexpectedly.");
        }
    }

    private static void WriteMultiplexHeader(PipeWriter writer)
    {
        var span = writer.GetSpan(MultiplexHeaderLength);
        WriteMultiplexHeader(span[..MultiplexHeaderLength]);
        writer.Advance(MultiplexHeaderLength);
    }

    private static void WriteMultiplexHeader(Span<byte> span)
    {
        BinaryPrimitives.WriteInt32BigEndian(span[..4], MultiplexMagic);
        span[4] = MultiplexVersion;
    }

    private static void WriteFrameHeader(
        PipeWriter writer,
        BusFrameType frameType,
        Guid channelId,
        int payloadLength)
    {
        var span = writer.GetSpan(FrameHeaderLength);
        WriteFrameHeader(span[..FrameHeaderLength], frameType, channelId, payloadLength);
        writer.Advance(FrameHeaderLength);
    }

    private static void WriteFrameHeader(
        Span<byte> span,
        BusFrameType frameType,
        Guid channelId,
        int payloadLength)
    {
        span[0] = (byte)frameType;
        channelId.TryWriteBytes(span.Slice(1, 16));
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(17, 4), payloadLength);
    }

    private static bool IsMultiplexStream(ReadOnlySpan<byte> header)
    {
        if (header.Length < MultiplexHeaderLength)
        {
            return false;
        }

        return BinaryPrimitives.ReadInt32BigEndian(header[..4]) == MultiplexMagic &&
               header[4] == MultiplexVersion;
    }

    private static Guid ResolveChannelId(Guid frameChannelId, string? channelIdText)
    {
        if (frameChannelId != Guid.Empty)
        {
            return frameChannelId;
        }

        return Guid.TryParse(channelIdText, out var channelId) ? channelId : Guid.Empty;
    }

    private static bool IsTerminalEnvelope(string route, string? command)
    {
        if (!string.Equals(route, RouteFile, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalized = NormalizeFileCommand(command);
        return normalized is FileCommandEnd or FileCommandCancel;
    }

    private static string ResolveRoute(string? route, string? command)
    {
        if (!string.IsNullOrWhiteSpace(route))
        {
            return route.Trim().ToLowerInvariant();
        }

        var normalized = command?.Trim().ToLowerInvariant();
        return normalized switch
        {
            FileCommandBegin or FileCommandEnd or FileCommandCancel or LegacyStartFileCommand or
                LegacyFinishFileCommand or LegacyCancelFileCommand => RouteFile,
            MessageCommandPublish or RouteMessage => RouteMessage,
            _ => RouteCommand
        };
    }

    private static string NormalizeFileCommand(string? command)
    {
        var normalized = command?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized switch
        {
            LegacyStartFileCommand => FileCommandBegin,
            LegacyFinishFileCommand => FileCommandEnd,
            LegacyCancelFileCommand => FileCommandCancel,
            _ => normalized
        };
    }

    private static ChannelContextKey CreateChannelContextKey(BusRouteContext context, Guid channelId)
    {
        var address = NormalizeAddress(context.RemoteEndPoint.Address);
        return new ChannelContextKey(context.Protocol, address, context.RemoteEndPoint.Port, channelId);
    }

    private static IPAddress NormalizeAddress(IPAddress address)
    {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    private static BusEnvelope CreateFileEnvelope(
        Guid channelId,
        string command,
        string? fileName,
        Stream? stream)
    {
        Dictionary<string, string?>? metadata = null;
        if (stream is not null && stream.CanSeek)
        {
            metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["length"] = (stream.Length - stream.Position).ToString()
            };
        }

        return new BusEnvelope
        {
            Route = RouteFile,
            Command = command,
            ChannelId = channelId.ToString("D"),
            ContentType = "application/octet-stream",
            FileName = fileName,
            Metadata = metadata
        };
    }

    private static bool TryParseEnvelope(ReadOnlySpan<byte> payload, out BusEnvelope envelope)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<BusEnvelope>(payload);
            if (parsed is null || (string.IsNullOrWhiteSpace(parsed.Route) && string.IsNullOrWhiteSpace(parsed.Command)))
            {
                envelope = new BusEnvelope();
                return false;
            }

            envelope = parsed;
            return true;
        }
        catch
        {
            envelope = new BusEnvelope();
            return false;
        }
    }

    private static async Task SaveLegacyPayloadAsync(
        LocalDataTransportProtocol protocol,
        IPEndPoint remoteEndPoint,
        byte[] prefixPayload,
        PipeReader payloadReader,
        CancellationToken cancellationToken)
    {
        var filePath = BuildLegacyFilePath(protocol, remoteEndPoint);
        await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await stream.WriteAsync(prefixPayload, cancellationToken);
        await payloadReader.CopyToAsync(stream, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task SaveMessageAsync(
        LocalDataTransportProtocol protocol,
        IPEndPoint remoteEndPoint,
        string message,
        CancellationToken cancellationToken)
    {
        var filePath = BuildMessageFilePath(protocol, remoteEndPoint);
        await File.WriteAllTextAsync(filePath, message, Encoding.UTF8, cancellationToken);
    }

    private static async Task SaveCommandAsync(
        LocalDataTransportProtocol protocol,
        IPEndPoint remoteEndPoint,
        BusEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var filePath = BuildCommandFilePath(protocol, remoteEndPoint);
        var json = JsonSerializer.Serialize(envelope);
        await File.WriteAllTextAsync(filePath, json, Encoding.UTF8, cancellationToken);
    }

    private static async Task SavePayloadToFileAsync(
        PipeReader payloadReader,
        int payloadLength,
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await CopyExactlyToStreamAsync(payloadReader, stream, payloadLength, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<byte[]?> ReadExactlyOrEndAsync(
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

            var toCopy = (int)System.Math.Min(sequence.Length, byteCount - filled);
            CopySequenceToSpan(sequence.Slice(0, toCopy), buffer.AsSpan(filled, toCopy));
            filled += toCopy;
            payloadReader.AdvanceTo(sequence.GetPosition(toCopy), sequence.End);
        }

        return buffer;
    }

    private static async Task<byte[]> ReadUpToAsync(
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

            var toCopy = (int)System.Math.Min(sequence.Length, maxByteCount - filled);
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

    private static async Task<byte[]> ReadExactlyAsync(
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

    private static async Task CopyExactlyToStreamAsync(
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

            var toCopy = (int)System.Math.Min(sequence.Length, remaining);
            foreach (var segment in sequence.Slice(0, toCopy))
            {
                await stream.WriteAsync(segment, cancellationToken);
            }

            remaining -= toCopy;
            payloadReader.AdvanceTo(sequence.GetPosition(toCopy), sequence.End);
        }
    }

    private static async Task DrainExactlyAsync(
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

            var consumed = (int)System.Math.Min(sequence.Length, remaining);
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

    private static string BuildLegacyFilePath(LocalDataTransportProtocol protocol, IPEndPoint remoteEndPoint)
    {
        var remoteAddress = remoteEndPoint.Address.ToString().Replace(':', '_');
        var fileName = $"recv-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{protocol}-{remoteAddress}-{remoteEndPoint.Port}.bin";
        return Path.Combine(Core.Utils.KitopiaPaths.ReceivedFilesDirectory, fileName);
    }

    private static string BuildTransferFilePath(
        LocalDataTransportProtocol protocol,
        IPEndPoint remoteEndPoint,
        Guid channelId,
        string? targetFileName)
    {
        var remoteAddress = remoteEndPoint.Address.ToString().Replace(':', '_');
        var fileName = string.IsNullOrWhiteSpace(targetFileName)
            ? $"transfer-{channelId:N}.bin"
            : SanitizeFileName(targetFileName);
        var file = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{protocol}-{remoteAddress}-{remoteEndPoint.Port}-{fileName}";
        return Path.Combine(Core.Utils.KitopiaPaths.ReceivedFilesDirectory, file);
    }

    private static string BuildMessageFilePath(LocalDataTransportProtocol protocol, IPEndPoint remoteEndPoint)
    {
        var remoteAddress = remoteEndPoint.Address.ToString().Replace(':', '_');
        var fileName = $"message-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{protocol}-{remoteAddress}-{remoteEndPoint.Port}.txt";
        return Path.Combine(Core.Utils.KitopiaPaths.ReceivedFilesDirectory, fileName);
    }

    private static string BuildCommandFilePath(LocalDataTransportProtocol protocol, IPEndPoint remoteEndPoint)
    {
        var remoteAddress = remoteEndPoint.Address.ToString().Replace(':', '_');
        var fileName = $"command-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{protocol}-{remoteAddress}-{remoteEndPoint.Port}.json";
        return Path.Combine(Core.Utils.KitopiaPaths.ReceivedFilesDirectory, fileName);
    }

    private static string BuildMessagePayloadFilePath(LocalDataTransportProtocol protocol, IPEndPoint remoteEndPoint)
    {
        var remoteAddress = remoteEndPoint.Address.ToString().Replace(':', '_');
        var fileName = $"message-payload-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{protocol}-{remoteAddress}-{remoteEndPoint.Port}.bin";
        return Path.Combine(Core.Utils.KitopiaPaths.ReceivedFilesDirectory, fileName);
    }

    private static string BuildCommandPayloadFilePath(LocalDataTransportProtocol protocol, IPEndPoint remoteEndPoint)
    {
        var remoteAddress = remoteEndPoint.Address.ToString().Replace(':', '_');
        var fileName = $"command-payload-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{protocol}-{remoteAddress}-{remoteEndPoint.Port}.bin";
        return Path.Combine(Core.Utils.KitopiaPaths.ReceivedFilesDirectory, fileName);
    }

    private static string SanitizeFileName(string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(safeName) ? "unnamed.bin" : safeName;
    }

    private static void ValidateSendContext(LocalDataSendContext sendContext)
    {
        ArgumentNullException.ThrowIfNull(sendContext.Listener);
        ArgumentNullException.ThrowIfNull(sendContext.RemoteEndPoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(sendContext.RemoteIdentityPublicKey);
    }
}
