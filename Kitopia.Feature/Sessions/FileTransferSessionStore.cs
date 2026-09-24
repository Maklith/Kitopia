namespace Kitopia.Feature.DeviceCommunication.Sessions;

public sealed class FileTransferSessionStore : IFileTransferSessionStore
{
    private const int MaximumPendingIncomingOffers = 128;
    private static readonly TimeSpan IncomingOfferLifetime = TimeSpan.FromMinutes(10);
    private readonly object _sync = new();
    private readonly Dictionary<Guid, FileTransferSession> _sessions = new();

    public bool TryAdd(FileTransferSession session)
    {
        lock (_sync)
        {
            if (_sessions.ContainsKey(session.TransferId))
            {
                return false;
            }

            if (session.IsIncoming && session.State == FileTransferState.Offered)
            {
                var now = DateTimeOffset.UtcNow;
                List<Guid>? expired = null;
                var pendingCount = 0;
                foreach (var pair in _sessions)
                {
                    var existing = pair.Value;
                    if (!existing.IsIncoming || existing.State != FileTransferState.Offered)
                    {
                        continue;
                    }

                    if (now - existing.CreatedAt >= IncomingOfferLifetime)
                    {
                        (expired ??= []).Add(pair.Key);
                        continue;
                    }

                    pendingCount++;
                }

                if (expired is not null)
                {
                    foreach (var id in expired)
                    {
                        _sessions.Remove(id);
                    }
                }

                if (pendingCount >= MaximumPendingIncomingOffers)
                {
                    return false;
                }
            }

            return _sessions.TryAdd(session.TransferId, session);
        }
    }

    public bool TryGet(Guid transferId, out FileTransferSession session)
    {
        lock (_sync)
        {
            return _sessions.TryGetValue(transferId, out session!);
        }
    }

    public bool TryAccept(Guid transferId, string conversationId, string savePath,
        Func<CancellationToken, ValueTask<Stream>>? openWriteStreamAsync)
    {
        lock (_sync)
        {
            if (!_sessions.TryGetValue(transferId, out var session) ||
                session.State != FileTransferState.Offered ||
                !session.IsIncoming ||
                !string.Equals(session.ConversationId, conversationId, StringComparison.Ordinal) ||
                session.SizeBytes < 0)
            {
                return false;
            }

            if (DateTimeOffset.UtcNow - session.CreatedAt >= IncomingOfferLifetime)
            {
                _sessions.Remove(transferId);
                return false;
            }

            session.SavePath = savePath;
            session.OpenWriteStreamAsync = openWriteStreamAsync;
            session.State = FileTransferState.Accepted;
            return true;
        }
    }

    public bool TryBeginReceive(Guid transferId, string conversationId, long? sizeBytes,
        out FileTransferSession session, out CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (!_sessions.TryGetValue(transferId, out session!) ||
                !session.IsIncoming ||
                session.State != FileTransferState.Accepted ||
                !string.Equals(session.ConversationId, conversationId, StringComparison.Ordinal) ||
                session.SizeBytes != sizeBytes ||
                (string.IsNullOrWhiteSpace(session.SavePath) && session.OpenWriteStreamAsync is null))
            {
                session = null!;
                cancellationToken = default;
                return false;
            }

            session.ReceiveCancellation = new CancellationTokenSource();
            session.State = FileTransferState.Receiving;
            cancellationToken = session.ReceiveCancellation.Token;
            return true;
        }
    }

    public bool TryRemoveIncomingOffer(Guid transferId, string conversationId)
    {
        lock (_sync)
        {
            return _sessions.TryGetValue(transferId, out var session) &&
                   session.IsIncoming &&
                   session.State == FileTransferState.Offered &&
                   string.Equals(session.ConversationId, conversationId, StringComparison.Ordinal) &&
                   _sessions.Remove(transferId);
        }
    }

    public bool TryCancelIncoming(Guid transferId, string conversationId)
    {
        lock (_sync)
        {
            if (!_sessions.TryGetValue(transferId, out var session) ||
                !session.IsIncoming ||
                !string.Equals(session.ConversationId, conversationId, StringComparison.Ordinal))
            {
                return false;
            }

            if (session.State == FileTransferState.Receiving)
            {
                session.ReceiveCancellation?.Cancel();
                return true;
            }

            return session.State is FileTransferState.Offered or FileTransferState.Accepted &&
                   _sessions.Remove(transferId);
        }
    }

    public bool TryUpdateState(Guid transferId, FileTransferState expected, FileTransferState next)
    {
        lock (_sync)
        {
            if (!_sessions.TryGetValue(transferId, out var session) || session.State != expected)
            {
                return false;
            }

            session.State = next;
            return true;
        }
    }

    public bool TryRemove(Guid transferId, out FileTransferSession session)
    {
        lock (_sync)
        {
            return _sessions.Remove(transferId, out session!);
        }
    }
}
