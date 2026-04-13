using Core.Services.DeviceCommunication.Application;
using Core.Services.DeviceCommunication.Messages.Chat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class IncomingMessageBufferTests
{
    [TestMethod]
    public async Task PublishEventAsync_FileAccept_ShouldResolveWaiter()
    {
        var buffer = new IncomingMessageBuffer();
        var transferId = Guid.NewGuid();
        var waitTask = buffer.WaitForDecisionAsync(transferId, TimeSpan.FromSeconds(2));

        await buffer.PublishEventAsync(new IncomingMessageEvent(new FileAcceptChatMessage("peer", transferId)));

        var accepted = await waitTask;
        Assert.IsTrue(accepted);
    }

    [TestMethod]
    public async Task PublishEventAsync_FileReject_ShouldResolveWaiterAsRejected()
    {
        var buffer = new IncomingMessageBuffer();
        var transferId = Guid.NewGuid();
        var waitTask = buffer.WaitForDecisionAsync(transferId, TimeSpan.FromSeconds(2));

        await buffer.PublishEventAsync(new IncomingMessageEvent(new FileRejectChatMessage("peer", transferId, "r")));

        var accepted = await waitTask;
        Assert.IsFalse(accepted);
    }
}
