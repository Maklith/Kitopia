using System.Linq;
using Core.Services;
using Serilog;
using Serilog.Core;

namespace Core.Services.DeviceCommunication;

public sealed class LocalDataBusService : ILocalDataBusService
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<LocalDataBusService>();

    private readonly object _sync = new();
    private readonly ILocalDataStreamControl _streamControl;
    private readonly Dictionary<Type, ILocalDataBusMessageCodec> _codecsByType = [];
    private readonly Dictionary<string, List<ILocalDataBusMessageCodec>> _codecsByRoute =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Type, Dictionary<Guid, IBusTypedSubscription>> _subscriptions = [];
    private bool _isStarted;

    public LocalDataBusService(
        ILocalDataStreamControl streamControl,
        IEnumerable<ILocalDataBusMessageCodec> codecs)
    {
        _streamControl = streamControl;
        foreach (var codec in codecs)
        {
            RegisterCodec(codec);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_isStarted)
            {
                return Task.CompletedTask;
            }

            _streamControl.EnvelopeReceived += OnEnvelopeReceived;
            _isStarted = true;
        }

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        lock (_sync)
        {
            if (!_isStarted)
            {
                return Task.CompletedTask;
            }

            _streamControl.EnvelopeReceived -= OnEnvelopeReceived;
            _isStarted = false;
        }

        return Task.CompletedTask;
    }

    public IDisposable Subscribe<TMessage>(EventHandler<LocalDataBusMessageReceivedEventArgs<TMessage>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var messageType = typeof(TMessage);
        if (!_codecsByType.ContainsKey(messageType))
        {
            throw new InvalidOperationException(
                $"No bus message codec is registered for message type {messageType.FullName}.");
        }

        var id = Guid.NewGuid();
        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(messageType, out var bucket))
            {
                bucket = [];
                _subscriptions[messageType] = bucket;
            }

            bucket[id] = new BusTypedSubscription<TMessage>(handler);
        }

        return new LocalDataBusSubscription(this, messageType, id);
    }

    public Task PublishAsync<TMessage>(
        LocalDataBusSendContext sendContext,
        TMessage message,
        CancellationToken cancellationToken = default)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        var messageType = typeof(TMessage);
        if (!_codecsByType.TryGetValue(messageType, out var codec))
        {
            throw new InvalidOperationException(
                $"No bus message codec is registered for message type {messageType.FullName}.");
        }

        if (!codec.TryEncode(message, out var envelope))
        {
            throw new InvalidOperationException($"Bus codec failed to encode message type {messageType.FullName}.");
        }

        return SendEnvelopeCoreAsync(sendContext, envelope, cancellationToken);
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    private void RegisterCodec(ILocalDataBusMessageCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);

        if (_codecsByType.ContainsKey(codec.MessageType))
        {
            throw new InvalidOperationException(
                $"Duplicate bus codec for message type {codec.MessageType.FullName}.");
        }

        _codecsByType[codec.MessageType] = codec;

        if (!_codecsByRoute.TryGetValue(codec.Route, out var codecs))
        {
            codecs = [];
            _codecsByRoute[codec.Route] = codecs;
        }

        codecs.Add(codec);
    }

    private Task SendEnvelopeCoreAsync(
        LocalDataBusSendContext sendContext,
        LocalDataBusEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var snapshot = envelope.CreateSendSnapshot();
        var sendContextSnapshot = new LocalDataSendContext(
            sendContext.Listener,
            sendContext.Protocol,
            sendContext.RemoteEndPoint,
            sendContext.RemoteIdentityPublicKey);

        Guid? channelId = null;
        if (!string.IsNullOrWhiteSpace(snapshot.ChannelId) &&
            Guid.TryParse(snapshot.ChannelId, out var parsedChannelId) &&
            parsedChannelId != Guid.Empty)
        {
            channelId = parsedChannelId;
        }

        return _streamControl.SendCommandAsync(
            sendContextSnapshot,
            snapshot.Route,
            snapshot.Command,
            snapshot.Metadata,
            channelId,
            snapshot.ContentType,
            snapshot.Message,
            snapshot.FileName,
            cancellationToken);
    }

    private void OnEnvelopeReceived(object? sender, LocalDataBusEnvelopeReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Envelope.Route))
        {
            return;
        }

        if (!_codecsByRoute.TryGetValue(e.Envelope.Route, out var routeCodecs) || routeCodecs.Count == 0)
        {
            return;
        }

        foreach (var codec in routeCodecs)
        {
            if (!codec.CanHandleCommand(e.Envelope.Command))
            {
                continue;
            }

            if (!codec.TryDecode(e, out var message))
            {
                continue;
            }

            DispatchTypedMessage(codec.MessageType, e, message);
        }
    }

    private void DispatchTypedMessage(
        Type messageType,
        LocalDataBusEnvelopeReceivedEventArgs envelopeArgs,
        object message)
    {
        List<IBusTypedSubscription>? subscriptions = null;
        lock (_sync)
        {
            if (_subscriptions.TryGetValue(messageType, out var bucket) && bucket.Count > 0)
            {
                subscriptions = bucket.Values.ToList();
            }
        }

        if (subscriptions is null)
        {
            return;
        }

        foreach (var subscription in subscriptions)
        {
            try
            {
                subscription.Invoke(this, envelopeArgs, message);
            }
            catch (Exception ex)
            {
                Logger.Warning(
                    ex,
                    "LocalDataBusService typed subscriber failed. MessageType={MessageType}, Protocol={Protocol}, RemoteEndPoint={RemoteEndPoint}, Route={Route}, Command={Command}",
                    messageType.Name,
                    envelopeArgs.Protocol,
                    envelopeArgs.RemoteEndPoint,
                    envelopeArgs.Envelope.Route,
                    envelopeArgs.Envelope.Command);
            }
        }
    }

    private void Unsubscribe(Type messageType, Guid subscriptionId)
    {
        lock (_sync)
        {
            if (!_subscriptions.TryGetValue(messageType, out var bucket))
            {
                return;
            }

            bucket.Remove(subscriptionId);
            if (bucket.Count == 0)
            {
                _subscriptions.Remove(messageType);
            }
        }
    }

    private interface IBusTypedSubscription
    {
        void Invoke(object sender, LocalDataBusEnvelopeReceivedEventArgs envelopeArgs, object message);
    }

    private sealed class BusTypedSubscription<TMessage> : IBusTypedSubscription
    {
        private readonly EventHandler<LocalDataBusMessageReceivedEventArgs<TMessage>> _handler;

        public BusTypedSubscription(EventHandler<LocalDataBusMessageReceivedEventArgs<TMessage>> handler)
        {
            _handler = handler;
        }

        public void Invoke(object sender, LocalDataBusEnvelopeReceivedEventArgs envelopeArgs, object message)
        {
            if (message is not TMessage typedMessage)
            {
                return;
            }

            var args = new LocalDataBusMessageReceivedEventArgs<TMessage>(
                envelopeArgs.Protocol,
                envelopeArgs.RemoteEndPoint,
                typedMessage,
                envelopeArgs.TimestampUtc);
            _handler.Invoke(sender, args);
        }
    }

    private sealed class LocalDataBusSubscription : IDisposable
    {
        private readonly LocalDataBusService _owner;
        private readonly Type _messageType;
        private readonly Guid _subscriptionId;
        private bool _disposed;

        public LocalDataBusSubscription(LocalDataBusService owner, Type messageType, Guid subscriptionId)
        {
            _owner = owner;
            _messageType = messageType;
            _subscriptionId = subscriptionId;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner.Unsubscribe(_messageType, _subscriptionId);
        }
    }
}
