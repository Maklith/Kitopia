using Kitopia.Feature.DeviceCommunication.Application;
using Kitopia.Feature.DeviceCommunication.Messages.Chat;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class SharedIncomingMessageBufferTests
{
    [TestMethod]
    public async Task PublishAsync_WhenReceiveQueueIsFull_WaitsForConsumer()
    {
        var buffer = new IncomingMessageBuffer();
        for (var i = 0; i < 8; i++)
        {
            await buffer.PublishAsync(new TextChatMessage("peer-1", i.ToString()));
        }

        var pending = buffer.PublishAsync(new TextChatMessage("peer-1", "last")).AsTask();
        Assert.IsFalse(pending.IsCompleted);

        await using var receiver = buffer.ReceiveAsync().GetAsyncEnumerator();
        Assert.IsTrue(await receiver.MoveNextAsync());
        await pending.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [TestMethod]
    public async Task PublishAsync_FileAcceptMessage_CompletesPendingDecision()
    {
        var buffer = new IncomingMessageBuffer();
        var transferId = Guid.NewGuid();

        await buffer.PublishAsync(new FileAcceptChatMessage("peer-1", transferId));
        var decision = await buffer.WaitForDecisionAsync(transferId, "peer-1", TimeSpan.FromSeconds(1));

        Assert.AreEqual(TransferDecision.Accepted, decision);
    }

    [TestMethod]
    public async Task WaitForDecisionAsync_FileRejectMessage_CompletesActiveWaiter()
    {
        var buffer = new IncomingMessageBuffer();
        var transferId = Guid.NewGuid();
        var waitTask = buffer.WaitForDecisionAsync(transferId, "peer-1", TimeSpan.FromSeconds(1));

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
        var receipt = await buffer.WaitForOfferReceiptAsync(transferId, "peer-1", TimeSpan.FromSeconds(1));
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

        var receipt = await buffer.WaitForOfferReceiptAsync(Guid.NewGuid(), "peer-1", TimeSpan.FromMilliseconds(20));

        Assert.AreEqual(TransferOfferReceipt.Timeout, receipt);
    }

    [TestMethod]
    public async Task WaitForDecisionAsync_OtherPeerCannotCompleteActiveWaiter()
    {
        var buffer = new IncomingMessageBuffer();
        var transferId = Guid.NewGuid();
        var waitTask = buffer.WaitForDecisionAsync(transferId, "peer-1", TimeSpan.FromSeconds(1));

        await buffer.PublishAsync(new FileRejectChatMessage("peer-2", transferId, "rejected_by_user"));
        await buffer.PublishAsync(new FileAcceptChatMessage("peer-1", transferId));

        Assert.AreEqual(TransferDecision.Accepted, await waitTask);
    }

    [TestMethod]
    public async Task WaitForDecisionAsync_OtherPeerCannotReplacePendingDecision()
    {
        var buffer = new IncomingMessageBuffer();
        var transferId = Guid.NewGuid();

        await buffer.PublishAsync(new FileAcceptChatMessage("peer-1", transferId));
        await buffer.PublishAsync(new FileRejectChatMessage("peer-2", transferId, "rejected_by_user"));

        Assert.AreEqual(
            TransferDecision.Accepted,
            await buffer.WaitForDecisionAsync(transferId, "peer-1", TimeSpan.FromSeconds(1)));
    }

    [TestMethod]
    public async Task WaitForOfferReceiptAsync_OtherPeerCannotCompleteActiveWaiter()
    {
        var buffer = new IncomingMessageBuffer();
        var transferId = Guid.NewGuid();
        var waitTask = buffer.WaitForOfferReceiptAsync(transferId, "peer-1", TimeSpan.FromMilliseconds(20));

        await buffer.PublishAsync(new FileOfferReceivedChatMessage("peer-2", transferId));

        Assert.AreEqual(TransferOfferReceipt.Timeout, await waitTask);
    }

    [TestMethod]
    public async Task WaitForOfferReceiptAsync_OtherPeerCannotReplacePendingReceipt()
    {
        var buffer = new IncomingMessageBuffer();
        var transferId = Guid.NewGuid();

        await buffer.PublishAsync(new FileOfferReceivedChatMessage("peer-1", transferId));
        await buffer.PublishAsync(new FileOfferReceivedChatMessage("peer-2", transferId));

        Assert.AreEqual(
            TransferOfferReceipt.Received,
            await buffer.WaitForOfferReceiptAsync(transferId, "peer-1", TimeSpan.FromSeconds(1)));
    }

    [TestMethod]
    public async Task WaitForDecisionAsync_ExpiredPendingDecisionIsIgnored()
    {
        var timeProvider = new AdjustableTimeProvider();
        var buffer = new IncomingMessageBuffer(timeProvider);
        var transferId = Guid.NewGuid();

        await buffer.PublishAsync(new FileAcceptChatMessage("peer-1", transferId));
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var freshTransferId = Guid.NewGuid();
        await buffer.PublishAsync(new FileAcceptChatMessage("peer-1", freshTransferId));

        Assert.AreEqual(
            TransferDecision.Timeout,
            await buffer.WaitForDecisionAsync(transferId, "peer-1", TimeSpan.FromMilliseconds(20)));
        Assert.AreEqual(
            TransferDecision.Accepted,
            await buffer.WaitForDecisionAsync(freshTransferId, "peer-1", TimeSpan.FromSeconds(1)));
    }

    [TestMethod]
    public async Task WaitForOfferReceiptAsync_ExpiredPendingReceiptIsIgnored()
    {
        var timeProvider = new AdjustableTimeProvider();
        var buffer = new IncomingMessageBuffer(timeProvider);
        var transferId = Guid.NewGuid();

        await buffer.PublishAsync(new FileOfferReceivedChatMessage("peer-1", transferId));
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        var freshTransferId = Guid.NewGuid();
        await buffer.PublishAsync(new FileOfferReceivedChatMessage("peer-1", freshTransferId));

        Assert.AreEqual(
            TransferOfferReceipt.Timeout,
            await buffer.WaitForOfferReceiptAsync(transferId, "peer-1", TimeSpan.FromMilliseconds(20)));
        Assert.AreEqual(
            TransferOfferReceipt.Received,
            await buffer.WaitForOfferReceiptAsync(freshTransferId, "peer-1", TimeSpan.FromSeconds(1)));
    }

    [TestMethod]
    public async Task WaitForDecisionAsync_PendingDecisionsEvictOldestAtCapacity()
    {
        var timeProvider = new AdjustableTimeProvider();
        var buffer = new IncomingMessageBuffer(timeProvider);
        await using var receiver = buffer.ReceiveAsync().GetAsyncEnumerator();
        var oldestTransferId = Guid.NewGuid();
        await buffer.PublishAsync(new FileAcceptChatMessage("peer-1", oldestTransferId));
        Assert.IsTrue(await receiver.MoveNextAsync());
        timeProvider.Advance(TimeSpan.FromMilliseconds(1));

        Guid newestTransferId = Guid.Empty;
        for (var index = 0; index < 256; index++)
        {
            newestTransferId = Guid.NewGuid();
            await buffer.PublishAsync(new FileAcceptChatMessage("peer-1", newestTransferId));
            Assert.IsTrue(await receiver.MoveNextAsync());
        }

        Assert.AreEqual(
            TransferDecision.Timeout,
            await buffer.WaitForDecisionAsync(oldestTransferId, "peer-1", TimeSpan.FromMilliseconds(20)));
        Assert.AreEqual(
            TransferDecision.Accepted,
            await buffer.WaitForDecisionAsync(newestTransferId, "peer-1", TimeSpan.FromSeconds(1)));
    }

    [TestMethod]
    public async Task WaitForOfferReceiptAsync_PendingReceiptsEvictOldestAtCapacity()
    {
        var timeProvider = new AdjustableTimeProvider();
        var buffer = new IncomingMessageBuffer(timeProvider);
        var oldestTransferId = Guid.NewGuid();
        await buffer.PublishAsync(new FileOfferReceivedChatMessage("peer-1", oldestTransferId));
        timeProvider.Advance(TimeSpan.FromMilliseconds(1));

        Guid newestTransferId = Guid.Empty;
        for (var index = 0; index < 256; index++)
        {
            newestTransferId = Guid.NewGuid();
            await buffer.PublishAsync(new FileOfferReceivedChatMessage("peer-1", newestTransferId));
        }

        Assert.AreEqual(
            TransferOfferReceipt.Timeout,
            await buffer.WaitForOfferReceiptAsync(oldestTransferId, "peer-1", TimeSpan.FromMilliseconds(20)));
        Assert.AreEqual(
            TransferOfferReceipt.Received,
            await buffer.WaitForOfferReceiptAsync(newestTransferId, "peer-1", TimeSpan.FromSeconds(1)));
    }

    private sealed class AdjustableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }
}
