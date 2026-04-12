namespace Core.Services.DeviceCommunication.Sessions;

public sealed class PayloadSessionStore : IPayloadSessionStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, PayloadSession> _sessions = new(StringComparer.Ordinal);

    public bool TryCreate(string remotePeer, Guid channelId, out PayloadSession session)
    {
        var key = BuildKey(remotePeer, channelId);
        lock (_sync)
        {
            if (_sessions.TryGetValue(key, out session!))
            {
                return false;
            }

            session = new PayloadSession(channelId);
            _sessions[key] = session;
            return true;
        }
    }

    public bool TryGet(string remotePeer, Guid channelId, out PayloadSession session)
    {
        var key = BuildKey(remotePeer, channelId);
        lock (_sync)
        {
            return _sessions.TryGetValue(key, out session!);
        }
    }

    public bool TryRemove(string remotePeer, Guid channelId, out PayloadSession session)
    {
        var key = BuildKey(remotePeer, channelId);
        lock (_sync)
        {
            if (!_sessions.Remove(key, out session!))
            {
                return false;
            }
        }

        return true;
    }

    private static string BuildKey(string remotePeer, Guid channelId)
    {
        return $"{remotePeer}|{channelId:D}";
    }
}
