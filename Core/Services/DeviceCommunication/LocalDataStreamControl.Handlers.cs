using System.IO.Pipelines;
using System.Net;

namespace Core.Services.DeviceCommunication;

public sealed partial class LocalDataStreamControl
{
    private enum BusFrameType : byte
    {
        Envelope = 1,
        Payload = 2
    }

    private enum FileTransferStage : byte
    {
        Initializing = 1,
        Receiving = 2,
        Completed = 3,
        Cancelled = 4
    }

    private readonly record struct BusRouteContext(LocalDataTransportProtocol Protocol, IPEndPoint RemoteEndPoint);

    private readonly record struct ChannelContextKey(
        LocalDataTransportProtocol Protocol,
        IPAddress Address,
        int Port,
        Guid ChannelId);

    private sealed class ChannelRouteBinding
    {
        public ChannelRouteBinding(string route, DateTime updatedUtc)
        {
            Route = route;
            UpdatedUtc = updatedUtc;
        }

        public string Route { get; }
        public DateTime UpdatedUtc { get; set; }
    }

    private sealed class BusEnvelope
    {
        public string? Route { get; set; }
        public string? Command { get; set; }
        public string? ChannelId { get; set; }
        public string? ContentType { get; set; }
        public string? FileName { get; set; }
        public string? Message { get; set; }
        public Dictionary<string, string?>? Metadata { get; set; }
    }

    private interface IBusRouteHandler
    {
        Task HandleEnvelopeAsync(
            BusRouteContext context,
            Guid channelId,
            BusEnvelope envelope,
            CancellationToken cancellationToken);

        Task HandlePayloadAsync(
            BusRouteContext context,
            Guid channelId,
            PipeReader payloadReader,
            int payloadLength,
            CancellationToken cancellationToken);

        Task CleanupAsync(DateTime nowUtc);
    }

    private sealed class FileRouteHandler : IBusRouteHandler
    {
        private readonly object _sync = new();
        private readonly Dictionary<ChannelContextKey, FileTransferSession> _sessions = [];

        public async Task HandleEnvelopeAsync(
            BusRouteContext context,
            Guid channelId,
            BusEnvelope envelope,
            CancellationToken cancellationToken)
        {
            var command = NormalizeFileCommand(envelope.Command);
            switch (command)
            {
                case FileCommandBegin:
                    if (channelId == Guid.Empty)
                    {
                        Logger.Warning(
                            "File begin envelope ignored because channel id is empty. Protocol={Protocol}, RemoteEndPoint={RemoteEndPoint}",
                            context.Protocol,
                            context.RemoteEndPoint);
                        return;
                    }

                    var key = CreateChannelContextKey(context, channelId);
                    var path = BuildTransferFilePath(context.Protocol, context.RemoteEndPoint, channelId, envelope.FileName);
                    await StartOrReplaceSessionAsync(key, path);
                    break;
                case FileCommandEnd:
                    if (channelId != Guid.Empty)
                    {
                        await CompleteSessionAsync(CreateChannelContextKey(context, channelId), deleteFile: false);
                    }
                    break;
                case FileCommandCancel:
                    if (channelId != Guid.Empty)
                    {
                        await CompleteSessionAsync(CreateChannelContextKey(context, channelId), deleteFile: true);
                    }
                    break;
                default:
                    await SaveCommandAsync(context.Protocol, context.RemoteEndPoint, envelope, cancellationToken);
                    break;
            }
        }

        public async Task HandlePayloadAsync(
            BusRouteContext context,
            Guid channelId,
            PipeReader payloadReader,
            int payloadLength,
            CancellationToken cancellationToken)
        {
            var effectiveChannelId = channelId == Guid.Empty ? Guid.NewGuid() : channelId;
            var key = CreateChannelContextKey(context, effectiveChannelId);
            var session = await GetOrCreateSessionAsync(key, context, effectiveChannelId);
            await CopyExactlyToStreamAsync(payloadReader, session.Stream, payloadLength, cancellationToken);
            session.UpdatedUtc = DateTime.UtcNow;
            session.Stage = FileTransferStage.Receiving;
        }

        public async Task CleanupAsync(DateTime nowUtc)
        {
            List<FileTransferSession>? expiredSessions = null;
            lock (_sync)
            {
                List<ChannelContextKey>? expiredKeys = null;
                foreach (var pair in _sessions)
                {
                    if (nowUtc - pair.Value.UpdatedUtc <= FileSessionTtl)
                    {
                        continue;
                    }

                    expiredKeys ??= [];
                    expiredKeys.Add(pair.Key);
                }

                if (expiredKeys is null)
                {
                    return;
                }

                expiredSessions = new List<FileTransferSession>(expiredKeys.Count);
                foreach (var key in expiredKeys)
                {
                    if (_sessions.Remove(key, out var session))
                    {
                        expiredSessions.Add(session);
                    }
                }
            }

            foreach (var session in expiredSessions!)
            {
                await session.DisposeAsync();
            }
        }

        private async Task StartOrReplaceSessionAsync(ChannelContextKey key, string filePath)
        {
            var nextSession = new FileTransferSession(
                filePath,
                new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                Stage = FileTransferStage.Initializing
            };

            FileTransferSession? previousSession = null;
            lock (_sync)
            {
                if (_sessions.TryGetValue(key, out var existing))
                {
                    previousSession = existing;
                }

                _sessions[key] = nextSession;
            }

            if (previousSession is not null)
            {
                await previousSession.DisposeAsync();
            }
        }

        private async Task<FileTransferSession> GetOrCreateSessionAsync(
            ChannelContextKey key,
            BusRouteContext context,
            Guid effectiveChannelId)
        {
            lock (_sync)
            {
                if (_sessions.TryGetValue(key, out var existing))
                {
                    return existing;
                }
            }

            var autoPath = BuildTransferFilePath(context.Protocol, context.RemoteEndPoint, effectiveChannelId, null);
            var created = new FileTransferSession(
                autoPath,
                new FileStream(autoPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                Stage = FileTransferStage.Receiving
            };

            FileTransferSession? existingSession = null;
            var inserted = false;
            lock (_sync)
            {
                if (_sessions.TryGetValue(key, out var existing))
                {
                    existingSession = existing;
                }
                else
                {
                    _sessions[key] = created;
                    inserted = true;
                }
            }

            if (!inserted)
            {
                await created.DisposeAsync();
                return existingSession!;
            }

            return created;
        }

        private async Task CompleteSessionAsync(ChannelContextKey key, bool deleteFile)
        {
            FileTransferSession? session = null;
            lock (_sync)
            {
                if (_sessions.Remove(key, out var removed))
                {
                    session = removed;
                }
            }

            if (session is null)
            {
                return;
            }

            session.Stage = deleteFile ? FileTransferStage.Cancelled : FileTransferStage.Completed;
            await session.DisposeAsync();
            if (deleteFile && File.Exists(session.FilePath))
            {
                File.Delete(session.FilePath);
            }
        }
    }

    private sealed class MessageRouteHandler : IBusRouteHandler
    {
        public async Task HandleEnvelopeAsync(
            BusRouteContext context,
            Guid channelId,
            BusEnvelope envelope,
            CancellationToken cancellationToken)
        {
            var message = envelope.Message;
            if (string.IsNullOrWhiteSpace(message) &&
                envelope.Metadata is not null &&
                envelope.Metadata.TryGetValue("message", out var fromMetadata))
            {
                message = fromMetadata;
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            await SaveMessageAsync(context.Protocol, context.RemoteEndPoint, message, cancellationToken);
        }

        public async Task HandlePayloadAsync(
            BusRouteContext context,
            Guid channelId,
            PipeReader payloadReader,
            int payloadLength,
            CancellationToken cancellationToken)
        {
            var payloadPath = BuildMessagePayloadFilePath(context.Protocol, context.RemoteEndPoint);
            await SavePayloadToFileAsync(payloadReader, payloadLength, payloadPath, cancellationToken);
        }

        public Task CleanupAsync(DateTime nowUtc)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class CommandRouteHandler : IBusRouteHandler
    {
        public async Task HandleEnvelopeAsync(
            BusRouteContext context,
            Guid channelId,
            BusEnvelope envelope,
            CancellationToken cancellationToken)
        {
            await SaveCommandAsync(context.Protocol, context.RemoteEndPoint, envelope, cancellationToken);
        }

        public async Task HandlePayloadAsync(
            BusRouteContext context,
            Guid channelId,
            PipeReader payloadReader,
            int payloadLength,
            CancellationToken cancellationToken)
        {
            var payloadPath = BuildCommandPayloadFilePath(context.Protocol, context.RemoteEndPoint);
            await SavePayloadToFileAsync(payloadReader, payloadLength, payloadPath, cancellationToken);
        }

        public Task CleanupAsync(DateTime nowUtc)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FileTransferSession : IAsyncDisposable
    {
        public FileTransferSession(string filePath, FileStream stream)
        {
            FilePath = filePath;
            Stream = stream;
            UpdatedUtc = DateTime.UtcNow;
            Stage = FileTransferStage.Initializing;
        }

        public string FilePath { get; }
        public FileStream Stream { get; }
        public DateTime UpdatedUtc { get; set; }
        public FileTransferStage Stage { get; set; }

        public async ValueTask DisposeAsync()
        {
            await Stream.DisposeAsync();
        }
    }
}
