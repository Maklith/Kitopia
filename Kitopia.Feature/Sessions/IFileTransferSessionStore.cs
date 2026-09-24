namespace Kitopia.Feature.DeviceCommunication.Sessions;

public interface IFileTransferSessionStore
{
    bool TryAdd(FileTransferSession session);
    bool TryGet(Guid transferId, out FileTransferSession session);
    bool TryAccept(Guid transferId, string conversationId, string savePath,
        Func<CancellationToken, ValueTask<Stream>>? openWriteStreamAsync);
    bool TryBeginReceive(Guid transferId, string conversationId, long? sizeBytes,
        out FileTransferSession session, out CancellationToken cancellationToken);
    bool TryRemoveIncomingOffer(Guid transferId, string conversationId);
    bool TryCancelIncoming(Guid transferId, string conversationId);
    bool TryUpdateState(Guid transferId, FileTransferState expected, FileTransferState next);
    bool TryRemove(Guid transferId, out FileTransferSession session);
}
