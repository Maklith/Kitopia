using System.Threading.Channels;
using Kitopia.Feature.DeviceCommunication.Messages;
using Kitopia.Feature.DeviceCommunication.Messages.Chat;

namespace Kitopia.Feature.DeviceCommunication.Application;

public enum TransferDecision
{
    Accepted = 1,
    Rejected = 2,
    Timeout = 3
}

public enum TransferOfferReceipt
{
    Received = 1,
    Timeout = 2
}

public sealed class IncomingMessageBuffer : IIncomingMessageSink
{
    private readonly Channel<DeviceMessageEvent> _channel = Channel.CreateBounded<DeviceMessageEvent>(1024);
    private readonly object _sync = new();
    private readonly Dictionary<Guid, TaskCompletionSource<TransferDecision>> _transferDecisions = new();
    private readonly Dictionary<Guid, TransferDecision> _pendingTransferDecisions = new();
    private readonly Dictionary<Guid, TaskCompletionSource<TransferOfferReceipt>> _offerReceipts = new();
    private readonly Dictionary<Guid, TransferOfferReceipt> _pendingOfferReceipts = new();

    public ValueTask PublishAsync(AppMessage message, CancellationToken cancellationToken = default)
    {
        TrackTransferDecision(message);
        if (message is FileOfferReceivedChatMessage)
        {
            return ValueTask.CompletedTask;
        }

        return _channel.Writer.WriteAsync(DeviceMessageEventFactory.FromMessage(message), cancellationToken);
    }

    public ValueTask PublishEventAsync(DeviceMessageEvent messageEvent, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(messageEvent, cancellationToken);
    }

    public IAsyncEnumerable<DeviceMessageEvent> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }

    public Task<TransferDecision> WaitForDecisionAsync(Guid transferId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_pendingTransferDecisions.TryGetValue(transferId, out var pendingDecision))
            {
                _pendingTransferDecisions.Remove(transferId);
                return Task.FromResult(pendingDecision);
            }
        }

        var completion = new TaskCompletionSource<TransferDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            _transferDecisions[transferId] = completion;
        }

        return WaitCoreAsync(transferId, completion, timeout, cancellationToken);
    }

    public Task<TransferOfferReceipt> WaitForOfferReceiptAsync(Guid transferId, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_pendingOfferReceipts.TryGetValue(transferId, out var pendingReceipt))
            {
                _pendingOfferReceipts.Remove(transferId);
                return Task.FromResult(pendingReceipt);
            }
        }

        var completion = new TaskCompletionSource<TransferOfferReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            _offerReceipts[transferId] = completion;
        }

        return WaitOfferReceiptCoreAsync(transferId, completion, timeout, cancellationToken);
    }

    private async Task<TransferDecision> WaitCoreAsync(
        Guid transferId,
        TaskCompletionSource<TransferDecision> completion,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            var completed = await Task.WhenAny(completion.Task, Task.Delay(timeout, cancellationToken));
            if (completed != completion.Task)
            {
                return TransferDecision.Timeout;
            }

            return await completion.Task;
        }
        finally
        {
            lock (_sync)
            {
                _transferDecisions.Remove(transferId);
            }
        }
    }

    private void TrackTransferDecision(AppMessage message)
    {
        Guid transferId;
        TransferDecision decision;

        switch (message)
        {
            case FileAcceptChatMessage fileAccept:
                transferId = fileAccept.TransferId;
                decision = TransferDecision.Accepted;
                break;
            case FileRejectChatMessage fileReject:
                transferId = fileReject.TransferId;
                decision = TransferDecision.Rejected;
                break;
            case FileOfferReceivedChatMessage fileOfferReceived:
                transferId = fileOfferReceived.TransferId;
                TrackOfferReceipt(transferId, TransferOfferReceipt.Received);
                return;
            default:
                return;
        }

        lock (_sync)
        {
            if (_transferDecisions.TryGetValue(transferId, out var waiter))
            {
                waiter.TrySetResult(decision);
                return;
            }

            _pendingTransferDecisions[transferId] = decision;
        }
    }

    private async Task<TransferOfferReceipt> WaitOfferReceiptCoreAsync(
        Guid transferId,
        TaskCompletionSource<TransferOfferReceipt> completion,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            var completed = await Task.WhenAny(completion.Task, Task.Delay(timeout, cancellationToken));
            if (completed != completion.Task)
            {
                return TransferOfferReceipt.Timeout;
            }

            return await completion.Task;
        }
        finally
        {
            lock (_sync)
            {
                _offerReceipts.Remove(transferId);
            }
        }
    }

    private void TrackOfferReceipt(Guid transferId, TransferOfferReceipt receipt)
    {
        lock (_sync)
        {
            if (_offerReceipts.TryGetValue(transferId, out var waiter))
            {
                waiter.TrySetResult(receipt);
                return;
            }

            _pendingOfferReceipts[transferId] = receipt;
        }
    }
}
