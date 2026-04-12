namespace Core.Services.DeviceCommunication.Sessions;

public sealed class FileTransferSessionStore : IFileTransferSessionStore
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, FileTransferSession> _sessions = new();

    public bool TryAdd(FileTransferSession session)
    {
        lock (_sync)
        {
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
