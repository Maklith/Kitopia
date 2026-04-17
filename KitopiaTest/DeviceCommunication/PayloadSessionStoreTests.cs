using Core.Services.DeviceCommunication.Sessions;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class PayloadSessionStoreTests
{
    [TestMethod]
    public void TryCreateAndTryGet_ReturnsSameSession()
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
    public void TryCreate_AllowsSameChannelId_ForDifferentPeers()
    {
        var store = new PayloadSessionStore();
        var channelId = Guid.NewGuid();

        var first = store.TryCreate("peer-a", channelId, out _);
        var second = store.TryCreate("peer-b", channelId, out _);

        Assert.IsTrue(first);
        Assert.IsTrue(second);
    }

    [TestMethod]
    public void TryRemove_RemovesSession()
    {
        var store = new PayloadSessionStore();
        var channelId = Guid.NewGuid();
        store.TryCreate("peer-a", channelId, out _);

        var removed = store.TryRemove("peer-a", channelId, out _);
        var foundAfterRemove = store.TryGet("peer-a", channelId, out _);

        Assert.IsTrue(removed);
        Assert.IsFalse(foundAfterRemove);
    }
}
