using Kitopia.Feature.DeviceCommunication.Sessions;

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
