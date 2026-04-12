using System.IO.Pipelines;
using System.Net;
using Core.Services;
using Serilog;
using Serilog.Core;

namespace Core.Services.DeviceCommunication;

public sealed partial class LocalDataStreamControl : ILocalDataStreamControl
{
    private const int MaxEnvelopePayloadLength = 1024 * 1024;

    private const string RouteFile = "file";
    private const string RouteMessage = "message";
    private const string RouteCommand = "command";

    private const string FileCommandBegin = "begin";
    private const string FileCommandEnd = "end";
    private const string FileCommandCancel = "cancel";

    private static readonly TimeSpan ChannelRouteTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ChannelCleanupInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FileSessionTtl = TimeSpan.FromMinutes(30);

    private static readonly ILogger Logger = LogManager.Logger.ForContext<LocalDataStreamControl>();

    private readonly object _channelSync = new();
    private readonly Dictionary<ChannelContextKey, ChannelRouteBinding> _channelRoutes = [];
    private readonly FileRouteHandler _fileRouteHandler;
    private readonly Dictionary<string, IBusRouteHandler> _routeHandlers;
    private DateTime _lastCleanupUtc = DateTime.UtcNow;

    public event EventHandler<LocalDataBusEnvelopeReceivedEventArgs>? EnvelopeReceived;

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

        var streamHeader = await LocalDataPipeIo.ReadUpToAsync(
            payloadReader,
            LocalDataFrameProtocol.MultiplexHeaderLength,
            cancellationToken);
        if (streamHeader.Length == 0)
        {
            return;
        }

        if (streamHeader.Length < LocalDataFrameProtocol.MultiplexHeaderLength ||
            !LocalDataFrameProtocol.IsMultiplexStream(streamHeader))
        {
            await LocalDataStreamStorage.SaveLegacyPayloadAsync(
                protocol,
                remoteEndPoint,
                streamHeader,
                payloadReader,
                cancellationToken);
            return;
        }

        while (true)
        {
            var frameHeader = await LocalDataPipeIo.ReadExactlyOrEndAsync(
                payloadReader,
                LocalDataFrameProtocol.FrameHeaderLength,
                cancellationToken);
            if (frameHeader is null)
            {
                break;
            }

            LocalDataFrameProtocol.ParseFrameHeader(
                frameHeader,
                out var frameTypeRaw,
                out var frameChannelId,
                out var payloadLength);
            var frameType = (BusFrameType)frameTypeRaw;
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
                    await LocalDataPipeIo.DrainExactlyAsync(payloadReader, payloadLength, cancellationToken);
                    break;
            }
        }
    }

    public Task SendCommandAsync(
        LocalDataSendContext sendContext,
        string route,
        string command,
        IReadOnlyDictionary<string, string?>? metadata = null,
        Guid? channelId = null,
        string? contentType = null,
        string? message = null,
        string? fileName = null,
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
            FileName = string.IsNullOrWhiteSpace(fileName) ? null : fileName.Trim(),
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

        var payload = await LocalDataPipeIo.ReadExactlyAsync(payloadReader, payloadLength, cancellationToken);
        if (!LocalDataBusEnvelopeSerializer.TryDeserialize(
                payload,
                static parsed =>
                    !string.IsNullOrWhiteSpace(parsed.Route) || !string.IsNullOrWhiteSpace(parsed.Command),
                out BusEnvelope envelope))
        {
            return;
        }

        var context = new BusRouteContext(protocol, remoteEndPoint);
        var channelId = LocalDataStreamRouteResolver.ResolveChannelId(frameChannelId, envelope.ChannelId);
        var route = LocalDataStreamRouteResolver.ResolveRoute(envelope.Route, envelope.Command);
        envelope.Route = route;
        if (channelId != Guid.Empty)
        {
            envelope.ChannelId = channelId.ToString("D");
            SetChannelRoute(context, channelId, route);
        }

        PublishEnvelopeReceived(context, channelId, envelope);

        var routeHandler = ResolveRouteHandler(route);
        await routeHandler.HandleEnvelopeAsync(context, channelId, envelope, cancellationToken);
        if (channelId != Guid.Empty && LocalDataStreamRouteResolver.IsTerminalEnvelope(route, envelope.Command))
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

    private void PublishEnvelopeReceived(
        BusRouteContext context,
        Guid channelId,
        BusEnvelope envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.Route))
        {
            return;
        }

        var normalizedAddress = NormalizeAddress(context.RemoteEndPoint.Address);
        var snapshotEndPoint = new IPEndPoint(normalizedAddress, context.RemoteEndPoint.Port);
        var envelopeSnapshot = new LocalDataBusEnvelope
        {
            Route = envelope.Route ?? string.Empty,
            Command = envelope.Command ?? string.Empty,
            ChannelId = envelope.ChannelId,
            ContentType = envelope.ContentType,
            FileName = envelope.FileName,
            Message = envelope.Message,
            Metadata = envelope.Metadata is null ? null : new Dictionary<string, string?>(envelope.Metadata)
        };

        var args = new LocalDataBusEnvelopeReceivedEventArgs(
            context.Protocol,
            snapshotEndPoint,
            channelId,
            envelopeSnapshot,
            DateTimeOffset.UtcNow);
        var handlers = EnvelopeReceived;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<LocalDataBusEnvelopeReceivedEventArgs>)handler).Invoke(this, args);
            }
            catch (Exception ex)
            {
                Logger.Warning(
                    ex,
                    "EnvelopeReceived handler failed. Protocol={Protocol}, RemoteEndPoint={RemoteEndPoint}, Route={Route}",
                    context.Protocol,
                    snapshotEndPoint,
                    envelopeSnapshot.Route);
            }
        }
    }
}
