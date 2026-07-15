namespace Kitopia.Feature.DeviceCommunication.Sessions;

public interface IFileTransferSessionStore
{
    bool TryAdd(FileTransferSession session);
    bool TryGet(Guid transferId, out FileTransferSession session);
    bool TryUpdateState(Guid transferId, FileTransferState expected, FileTransferState next);
    bool TryRemove(Guid transferId, out FileTransferSession session);
}
