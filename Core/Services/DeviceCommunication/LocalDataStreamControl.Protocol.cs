using System.Buffers;
using System.IO.Pipelines;
using System.Net;

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
                LocalDataFrameProtocol.WriteMultiplexHeader(pipe.Writer);
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
        var payload = LocalDataBusEnvelopeSerializer.Serialize(envelope);
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
        var packetLength = LocalDataFrameProtocol.MultiplexHeaderLength + LocalDataFrameProtocol.FrameHeaderLength +
                           payload.Length;
        var rented = ArrayPool<byte>.Shared.Rent(packetLength);
        try
        {
            var span = rented.AsSpan(0, packetLength);
            LocalDataFrameProtocol.WriteMultiplexHeader(span[..LocalDataFrameProtocol.MultiplexHeaderLength]);
            LocalDataFrameProtocol.WriteFrameHeader(
                span.Slice(LocalDataFrameProtocol.MultiplexHeaderLength, LocalDataFrameProtocol.FrameHeaderLength),
                (byte)frameType,
                channelId,
                payload.Length);
            payload.Span.CopyTo(
                span[(LocalDataFrameProtocol.MultiplexHeaderLength + LocalDataFrameProtocol.FrameHeaderLength)..]);

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
        var payload = LocalDataBusEnvelopeSerializer.Serialize(envelope);
        LocalDataFrameProtocol.WriteFrameHeader(writer, (byte)BusFrameType.Envelope, channelId, payload.Length);
        writer.Write(payload);
        await EnsureFlushedAsync(writer, cancellationToken);
    }

    private static async Task WritePayloadFrameAsync(
        PipeWriter writer,
        Guid channelId,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        LocalDataFrameProtocol.WriteFrameHeader(writer, (byte)BusFrameType.Payload, channelId, payload.Length);
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

    private static void ValidateSendContext(LocalDataSendContext sendContext)
    {
        ArgumentNullException.ThrowIfNull(sendContext.Listener);
        ArgumentNullException.ThrowIfNull(sendContext.RemoteEndPoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(sendContext.RemoteIdentityPublicKey);
    }
}
