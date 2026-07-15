using Kitopia.Feature.DeviceCommunication.Application;
using Kitopia.Feature.DeviceCommunication.Messages.Chat;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class SharedIncomingMessageBufferTests
{
    [TestMethod]
    public async Task PublishAsync_FileAcceptMessage_CompletesPendingDecision()
    {
        var buffer = new IncomingMessageBuffer();
        var transferId = Guid.NewGuid();

        await buffer.PublishAsync(new FileAcceptChatMessage("peer-1", transferId));
        var decision = await buffer.WaitForDecisionAsync(transferId, TimeSpan.FromSeconds(1));

        Assert.AreEqual(TransferDecision.Accepted, decision);
    }

    [TestMethod]
    public async Task WaitForDecisionAsync_FileRejectMessage_CompletesActiveWaiter()
    {
        var buffer = new IncomingMessageBuffer();
        var transferId = Guid.NewGuid();
        var waitTask = buffer.WaitForDecisionAsync(transferId, TimeSpan.FromSeconds(1));

        await buffer.PublishAsync(new FileRejectChatMessage("peer-1", transferId, "rejected_by_user"));
        var decision = await waitTask;

        Assert.AreEqual(TransferDecision.Rejected, decision);
    }

    [TestMethod]
    public async Task PublishAsync_FileOfferReceipt_CompletesReceiptWithoutPublishingChatEvent()
    {
        var buffer = new IncomingMessageBuffer();
        var transferId = Guid.NewGuid();

        await buffer.PublishAsync(new FileOfferReceivedChatMessage("peer-1", transferId));
        var receipt = await buffer.WaitForOfferReceiptAsync(transferId, TimeSpan.FromSeconds(1));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        var enumerator = buffer.ReceiveAsync(cts.Token).GetAsyncEnumerator();
        var hasEvent = false;
        try
        {
            hasEvent = await enumerator.MoveNextAsync();
        }
        catch (OperationCanceledException)
        {
        }

        Assert.AreEqual(TransferOfferReceipt.Received, receipt);
        Assert.IsFalse(hasEvent);
    }

    [TestMethod]
    public async Task WaitForOfferReceiptAsync_TimesOut_WhenNoReceiptArrives()
    {
        var buffer = new IncomingMessageBuffer();

        var receipt = await buffer.WaitForOfferReceiptAsync(Guid.NewGuid(), TimeSpan.FromMilliseconds(20));

        Assert.AreEqual(TransferOfferReceipt.Timeout, receipt);
    }
}
