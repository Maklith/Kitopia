using Core.Services.DeviceCommunication.Sessions;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class SessionTests
{
    #region FileTransferSessionStore

    [TestMethod]
    public void FileTransferStore_TryAdd_And_TryGet_ReturnsSameSession()
    {
        var store = new FileTransferSessionStore();
        var session = CreateSession();

        var added = store.TryAdd(session);
        var found = store.TryGet(session.TransferId, out var retrieved);

        Assert.IsTrue(added);
        Assert.IsTrue(found);
        Assert.AreSame(session, retrieved);
    }

    [TestMethod]
    public void FileTransferStore_TryAdd_ReturnsFalse_ForDuplicate()
    {
        var store = new FileTransferSessionStore();
        var session = CreateSession();

        Assert.IsTrue(store.TryAdd(session));
        Assert.IsFalse(store.TryAdd(session));
    }

    [TestMethod]
    public void FileTransferStore_TryGet_ReturnsFalse_ForMissingId()
    {
        var store = new FileTransferSessionStore();

        var found = store.TryGet(Guid.NewGuid(), out _);

        Assert.IsFalse(found);
    }

    [TestMethod]
    public void FileTransferStore_TryRemove_RemovesSession()
    {
        var store = new FileTransferSessionStore();
        var session = CreateSession();
        store.TryAdd(session);

        var removed = store.TryRemove(session.TransferId, out var removedSession);
        var foundAfterRemove = store.TryGet(session.TransferId, out _);

        Assert.IsTrue(removed);
        Assert.AreSame(session, removedSession);
        Assert.IsFalse(foundAfterRemove);
    }

    [TestMethod]
    public void FileTransferStore_TryRemove_ReturnsFalse_ForMissingId()
    {
        var store = new FileTransferSessionStore();

        var removed = store.TryRemove(Guid.NewGuid(), out _);

        Assert.IsFalse(removed);
    }

    [TestMethod]
    public void FileTransferStore_TryUpdateState_Updates_WhenExpectedMatches()
    {
        var store = new FileTransferSessionStore();
        var session = CreateSession(FileTransferState.Offered);
        store.TryAdd(session);

        var updated = store.TryUpdateState(session.TransferId, FileTransferState.Offered, FileTransferState.Accepted);

        Assert.IsTrue(updated);
        store.TryGet(session.TransferId, out var retrieved);
        Assert.AreEqual(FileTransferState.Accepted, retrieved.State);
    }

    [TestMethod]
    public void FileTransferStore_TryUpdateState_Fails_WhenExpectedDoesNotMatch()
    {
        var store = new FileTransferSessionStore();
        var session = CreateSession(FileTransferState.Accepted);
        store.TryAdd(session);

        var updated = store.TryUpdateState(session.TransferId, FileTransferState.Offered, FileTransferState.Completed);

        Assert.IsFalse(updated);
        store.TryGet(session.TransferId, out var retrieved);
        Assert.AreEqual(FileTransferState.Accepted, retrieved.State);
    }

    [TestMethod]
    public void FileTransferStore_TryUpdateState_Fails_WhenSessionMissing()
    {
        var store = new FileTransferSessionStore();

        var updated = store.TryUpdateState(Guid.NewGuid(), FileTransferState.Offered, FileTransferState.Accepted);

        Assert.IsFalse(updated);
    }

    [TestMethod]
    public void FileTransferStore_TryAdd_ThenRemove_ThenAdd_Succeeds()
    {
        var store = new FileTransferSessionStore();
        var session = CreateSession();

        store.TryAdd(session);
        store.TryRemove(session.TransferId, out _);
        var reAdded = store.TryAdd(session);

        Assert.IsTrue(reAdded);
    }

    [TestMethod]
    public void FileTransferStore_SupportsMultipleSessions()
    {
        var store = new FileTransferSessionStore();
        var s1 = CreateSession();
        var s2 = CreateSession();
        var s3 = CreateSession();

        store.TryAdd(s1);
        store.TryAdd(s2);
        store.TryAdd(s3);

        Assert.IsTrue(store.TryGet(s1.TransferId, out _));
        Assert.IsTrue(store.TryGet(s2.TransferId, out _));
        Assert.IsTrue(store.TryGet(s3.TransferId, out _));
    }

    #endregion

    #region PayloadSessionStore

    [TestMethod]
    public void PayloadStore_TryCreateAndTryGet_ReturnsSameSession()
    {
        var store = new PayloadSessionStore();
        var channelId = Guid.NewGuid();

        var created = store.TryCreate("peer-a", channelId, out var createdSession);
        var found = store.TryGet("peer-a", channelId, out var foundSession);

        Assert.IsTrue(created);
        Assert.IsTrue(found);
        Assert.AreSame(createdSession, foundSession);
    }

    [TestMethod]
    public void PayloadStore_TryCreate_ReturnsFalse_ForDuplicatePeerAndChannel()
    {
        var store = new PayloadSessionStore();
        var channelId = Guid.NewGuid();

        Assert.IsTrue(store.TryCreate("peer-a", channelId, out _));
        Assert.IsFalse(store.TryCreate("peer-a", channelId, out _));
    }

    [TestMethod]
    public void PayloadStore_TryCreate_AllowsSameChannelId_ForDifferentPeers()
    {
        var store = new PayloadSessionStore();
        var channelId = Guid.NewGuid();

        Assert.IsTrue(store.TryCreate("peer-a", channelId, out _));
        Assert.IsTrue(store.TryCreate("peer-b", channelId, out _));
    }

    [TestMethod]
    public void PayloadStore_TryCreate_AllowsSamePeer_WithDifferentChannels()
    {
        var store = new PayloadSessionStore();

        Assert.IsTrue(store.TryCreate("peer-a", Guid.NewGuid(), out _));
        Assert.IsTrue(store.TryCreate("peer-a", Guid.NewGuid(), out _));
    }

    [TestMethod]
    public void PayloadStore_TryRemove_RemovesSession()
    {
        var store = new PayloadSessionStore();
        var channelId = Guid.NewGuid();
        store.TryCreate("peer-a", channelId, out _);

        var removed = store.TryRemove("peer-a", channelId, out _);
        var foundAfterRemove = store.TryGet("peer-a", channelId, out _);

        Assert.IsTrue(removed);
        Assert.IsFalse(foundAfterRemove);
    }

    [TestMethod]
    public void PayloadStore_TryRemove_ReturnsFalse_ForMissingKey()
    {
        var store = new PayloadSessionStore();

        var removed = store.TryRemove("missing", Guid.NewGuid(), out _);

        Assert.IsFalse(removed);
    }

    [TestMethod]
    public void PayloadStore_TryGet_ReturnsFalse_ForMissingKey()
    {
        var store = new PayloadSessionStore();

        var found = store.TryGet("missing", Guid.NewGuid(), out _);

        Assert.IsFalse(found);
    }

    [TestMethod]
    public void PayloadStore_RemovedSession_CanBeCreatedAgain()
    {
        var store = new PayloadSessionStore();
        var channelId = Guid.NewGuid();

        store.TryCreate("peer-a", channelId, out _);
        store.TryRemove("peer-a", channelId, out _);
        var recreated = store.TryCreate("peer-a", channelId, out _);

        Assert.IsTrue(recreated);
    }

    #endregion

    #region PayloadSession

    [TestMethod]
    public void PayloadSession_HasCorrectChannelId()
    {
        var channelId = Guid.NewGuid();
        var session = new PayloadSession(channelId);

        Assert.AreEqual(channelId, session.ChannelId);
    }

    [TestMethod]
    public void PayloadSession_ReaderAndWriter_AreNotNull()
    {
        var session = new PayloadSession(Guid.NewGuid());

        Assert.IsNotNull(session.Reader);
        Assert.IsNotNull(session.Writer);
    }

    #endregion

    #region FileTransferSession Properties

    [TestMethod]
    public void FileTransferSession_DefaultState_IsOffered()
    {
        var session = CreateSession();
        Assert.AreEqual(FileTransferState.Offered, session.State);
    }

    [TestMethod]
    public void FileTransferSession_CreatedAt_IsRecent()
    {
        var before = DateTimeOffset.UtcNow;
        var session = CreateSession();
        var after = DateTimeOffset.UtcNow;

        Assert.IsTrue(session.CreatedAt >= before);
        Assert.IsTrue(session.CreatedAt <= after);
    }

    [TestMethod]
    public void FileTransferSession_State_IsMutable()
    {
        var session = CreateSession();
        session.State = FileTransferState.Accepted;
        Assert.AreEqual(FileTransferState.Accepted, session.State);
        session.State = FileTransferState.Completed;
        Assert.AreEqual(FileTransferState.Completed, session.State);
    }

    #endregion

    #region Helpers

    private static FileTransferSession CreateSession(FileTransferState initialState = FileTransferState.Offered)
    {
        return new FileTransferSession
        {
            ConversationId = "peer-1",
            TransferId = Guid.NewGuid(),
            FileName = "test.bin",
            SizeBytes = 1024,
            ContentType = "application/octet-stream",
            State = initialState,
            SavePath = "/tmp/test.bin"
        };
    }

    #endregion
}
