using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Net;
using Core.Services;
using Serilog;
using Serilog.Core;

namespace Core.Services.DeviceCommunication;

public sealed partial class LocalDataStreamControl : ILocalDataStreamControl
{
    private const int MultiplexMagic = 0x4B544D58; // KTMX
    private const byte MultiplexVersion = 1;
    private const int MultiplexHeaderLength = 5;
    private const int FrameHeaderLength = 21;
    private const int MaxEnvelopePayloadLength = 1024 * 1024;

    private const string RouteFile = "file";
    private const string RouteMessage = "message";
    private const string RouteCommand = "command";

    private const string FileCommandBegin = "begin";
    private const string FileCommandEnd = "end";
    private const string FileCommandCancel = "cancel";
    private const string MessageCommandPublish = "publish";

    private const string LegacyStartFileCommand = "start_file";
    private const string LegacyFinishFileCommand = "finish_file";
    private const string LegacyCancelFileCommand = "cancel_file";

    private static readonly TimeSpan ChannelRouteTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ChannelCleanupInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FileSessionTtl = TimeSpan.FromMinutes(30);

    private static readonly ILogger Logger = LogManager.Logger.ForContext<LocalDataStreamControl>();

    private readonly object _channelSync = new();
    private readonly Dictionary<ChannelContextKey, ChannelRouteBinding> _channelRoutes = [];
    private readonly FileRouteHandler _fileRouteHandler;
    private readonly Dictionary<string, IBusRouteHandler> _routeHandlers;
    private DateTime _lastCleanupUtc = DateTime.UtcNow;

    public LocalDataStreamControl()
    {
        _fileRouteHandler = new FileRouteHandler();
        _routeHandlers = new Dictionary<string, IBusRouteHandler>(StringComparer.OrdinalIgnoreCase)
        {
            [RouteFile] = _fileRouteHandler,
            [RouteMessage] = new MessageRouteHandler(),
            [RouteCommand] = new CommandRouteHandler()
        };
    }

    public async ValueTask HandleAsync(
        LocalDataTransportProtocol protocol,
        IPEndPoint remoteEndPoint,
        PipeReader payloadReader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remoteEndPoint);
        ArgumentNullException.ThrowIfNull(payloadReader);

        await CleanupStateAsync();

        var streamHeader = await ReadUpToAsync(payloadReader, MultiplexHeaderLength, cancellationToken);
        if (streamHeader.Length == 0)
        {
            return;
        }

        if (streamHeader.Length < MultiplexHeaderLength || !IsMultiplexStream(streamHeader))
        {
            await SaveLegacyPayloadAsync(protocol, remoteEndPoint, streamHeader, payloadReader, cancellationToken);
            return;
        }

        while (true)
        {
            var frameHeader = await ReadExactlyOrEndAsync(payloadReader, FrameHeaderLength, cancellationToken);
            if (frameHeader is null)
            {
                break;
            }

            var frameType = (BusFrameType)frameHeader[0];
            var frameChannelId = new Guid(frameHeader.AsSpan(1, 16));
            var payloadLength = BinaryPrimitives.ReadInt32BigEndian(frameHeader.AsSpan(17, 4));
            if (payloadLength < 0)
            {
                throw new InvalidDataException($"Invalid frame payload length: {payloadLength}.");
            }

            switch (frameType)
            {
                case BusFrameType.Envelope:
                    await ProcessEnvelopeFrameAsync(
                        protocol,
                        remoteEndPoint,
                        frameChannelId,
                        payloadLength,
                        payloadReader,
                        cancellationToken);
                    break;
                case BusFrameType.Payload:
                    await ProcessPayloadFrameAsync(
                        protocol,
                        remoteEndPoint,
                        frameChannelId,
                        payloadLength,
                        payloadReader,
                        cancellationToken);
                    break;
                default:
                    Logger.Warning(
                        "Unsupported bus frame type. Protocol={Protocol}, RemoteEndPoint={RemoteEndPoint}, FrameType={FrameType}",
                        protocol,
                        remoteEndPoint,
                        frameType);
                    await DrainExactlyAsync(payloadReader, payloadLength, cancellationToken);
                    break;
            }
        }
    }

    public Task SendMessageAsync(
        LocalDataSendContext sendContext,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return Task.CompletedTask;
        }

        var envelope = new BusEnvelope
        {
            Route = RouteMessage,
            Command = MessageCommandPublish,
            ContentType = "text/plain",
            Message = message
        };

        return SendEnvelopePacketAsync(sendContext, Guid.Empty, envelope, cancellationToken);
    }

    public Task SendCommandAsync(
        LocalDataSendContext sendContext,
        string route,
        string command,
        IReadOnlyDictionary<string, string?>? metadata = null,
        Guid? channelId = null,
        string? contentType = null,
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        var effectiveChannelId = channelId ?? Guid.Empty;
        var envelope = new BusEnvelope
        {
            Route = route.Trim().ToLowerInvariant(),
            Command = command.Trim(),
            ChannelId = effectiveChannelId == Guid.Empty ? null : effectiveChannelId.ToString("D"),
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/json" : contentType,
            Message = message,
            Metadata = metadata is null ? null : new Dictionary<string, string?>(metadata)
        };

        return SendEnvelopePacketAsync(sendContext, effectiveChannelId, envelope, cancellationToken);
    }

    public async Task SendFileAsync(
        LocalDataSendContext sendContext,
        Stream stream,
        string? fileName = null,
        int framePayloadSize = 64 * 1024,
        CancellationToken cancellationToken = default)
    {
        ValidateSendContext(sendContext);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new InvalidOperationException("Source stream is not readable.");
        }

        if (framePayloadSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(framePayloadSize), "Frame payload size must be greater than 0.");
        }

        await SendFileByStreamFramesAsync(sendContext, stream, fileName, framePayloadSize, cancellationToken);
    }

    private async Task ProcessEnvelopeFrameAsync(
        LocalDataTransportProtocol protocol,
        IPEndPoint remoteEndPoint,
        Guid frameChannelId,
        int payloadLength,
        PipeReader payloadReader,
        CancellationToken cancellationToken)
    {
        if (payloadLength == 0)
        {
            return;
        }

        if (payloadLength > MaxEnvelopePayloadLength)
        {
            throw new InvalidDataException($"Envelope payload is too large: {payloadLength}.");
        }

        var payload = await ReadExactlyAsync(payloadReader, payloadLength, cancellationToken);
        if (!TryParseEnvelope(payload, out var envelope))
        {
            return;
        }

        var context = new BusRouteContext(protocol, remoteEndPoint);
        var channelId = ResolveChannelId(frameChannelId, envelope.ChannelId);
        var route = ResolveRoute(envelope.Route, envelope.Command);
        envelope.Route = route;
        if (channelId != Guid.Empty)
        {
            envelope.ChannelId = channelId.ToString("D");
            SetChannelRoute(context, channelId, route);
        }

        var routeHandler = ResolveRouteHandler(route);
        await routeHandler.HandleEnvelopeAsync(context, channelId, envelope, cancellationToken);
        if (channelId != Guid.Empty && IsTerminalEnvelope(route, envelope.Command))
        {
            RemoveChannelRoute(context, channelId);
        }
    }

    private async Task ProcessPayloadFrameAsync(
        LocalDataTransportProtocol protocol,
        IPEndPoint remoteEndPoint,
        Guid frameChannelId,
        int payloadLength,
        PipeReader payloadReader,
        CancellationToken cancellationToken)
    {
        if (payloadLength == 0)
        {
            return;
        }

        var context = new BusRouteContext(protocol, remoteEndPoint);
        var route = RouteFile;
        if (frameChannelId != Guid.Empty && TryGetChannelRoute(context, frameChannelId, out var boundRoute))
        {
            route = boundRoute;
            TouchChannelRoute(context, frameChannelId);
        }

        var routeHandler = ResolveRouteHandler(route);
        await routeHandler.HandlePayloadAsync(context, frameChannelId, payloadReader, payloadLength, cancellationToken);
    }
}
