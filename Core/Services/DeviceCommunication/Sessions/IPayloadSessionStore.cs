namespace Core.Services.DeviceCommunication.Sessions;

public interface IPayloadSessionStore
{
    bool TryCreate(string remotePeer, Guid channelId, out PayloadSession session);
    bool TryGet(string remotePeer, Guid channelId, out PayloadSession session);
    bool TryRemove(string remotePeer, Guid channelId, out PayloadSession session);
}
