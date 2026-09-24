using Kitopia.Feature.DeviceCommunication.Sessions;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class SharedFileTransferSessionStoreTests
{
    [TestMethod]
    public void TryAccept_ExpiredOffer_DoesNotCreateAcceptedSession()
    {
        var store = new FileTransferSessionStore();
        var transferId = Guid.NewGuid();
        store.TryAdd(CreateIncomingOffer(transferId, DateTimeOffset.UtcNow.AddMinutes(-11)));

        Assert.IsFalse(store.TryAccept(transferId, "peer-1", "shared.bin", null));
        Assert.IsFalse(store.TryGet(transferId, out _));
    }

    [TestMethod]
    public void TryAccept_OutgoingOffer_DoesNotChangeState()
    {
        var store = new FileTransferSessionStore();
        var transferId = Guid.NewGuid();
        var outgoing = new FileTransferSession
        {
            ConversationId = "peer-1",
            TransferId = transferId,
            FileName = "shared.bin",
            SizeBytes = 4,
            State = FileTransferState.Offered
        };
        store.TryAdd(outgoing);

        Assert.IsFalse(store.TryAccept(transferId, "peer-1", "shared.bin", null));
        Assert.AreEqual(FileTransferState.Offered, outgoing.State);
    }

    [TestMethod]
    public void TryAdd_PendingOfferCapacity_RejectsNewOfferWithoutRemovingExisting()
    {
        var store = new FileTransferSessionStore();
        var oldestId = Guid.NewGuid();
        var oldest = CreateIncomingOffer(oldestId, DateTimeOffset.UtcNow.AddMinutes(-2));
        Assert.IsTrue(store.TryAdd(oldest));
        for (var i = 1; i < 128; i++)
        {
            Assert.IsTrue(store.TryAdd(CreateIncomingOffer(Guid.NewGuid(), DateTimeOffset.UtcNow)));
        }

        Assert.IsFalse(store.TryAdd(CreateIncomingOffer(oldestId, DateTimeOffset.UtcNow)));
        Assert.IsTrue(store.TryGet(oldestId, out _));
        Assert.IsFalse(store.TryAdd(CreateIncomingOffer(Guid.NewGuid(), DateTimeOffset.UtcNow)));
        Assert.IsTrue(store.TryGet(oldestId, out _));
    }

    [TestMethod]
    public void TryCancelIncoming_ReceivingSession_CancelsReceiveToken()
    {
        var store = new FileTransferSessionStore();
        var transferId = Guid.NewGuid();
        store.TryAdd(CreateIncomingOffer(transferId, DateTimeOffset.UtcNow));
        Assert.IsTrue(store.TryAccept(transferId, "peer-1", "shared.bin", null));
        Assert.IsTrue(store.TryBeginReceive(transferId, "peer-1", 4, out _, out var token));

        Assert.IsTrue(store.TryCancelIncoming(transferId, "peer-1"));
        Assert.IsTrue(token.IsCancellationRequested);
    }

    private static FileTransferSession CreateIncomingOffer(Guid transferId, DateTimeOffset createdAt)
    {
        return new FileTransferSession
        {
            ConversationId = "peer-1",
            TransferId = transferId,
            FileName = "shared.bin",
            SizeBytes = 4,
            IsIncoming = true,
            State = FileTransferState.Offered,
            CreatedAt = createdAt
        };
    }
}
