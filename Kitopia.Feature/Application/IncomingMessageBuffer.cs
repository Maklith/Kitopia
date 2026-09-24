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
    private const int MaxPendingResponses = 256;
    private static readonly TimeSpan PendingResponseLifetime = TimeSpan.FromSeconds(30);

    private readonly Channel<DeviceMessageEvent> _channel = Channel.CreateBounded<DeviceMessageEvent>(8);
    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<(Guid TransferId, string ConversationId), TaskCompletionSource<TransferDecision>> _transferDecisions = new();
    private readonly Dictionary<(Guid TransferId, string ConversationId), (TransferDecision Value, DateTimeOffset ReceivedAt)> _pendingTransferDecisions = new();
    private readonly Dictionary<(Guid TransferId, string ConversationId), TaskCompletionSource<TransferOfferReceipt>> _offerReceipts = new();
    private readonly Dictionary<(Guid TransferId, string ConversationId), (TransferOfferReceipt Value, DateTimeOffset ReceivedAt)> _pendingOfferReceipts = new();

    public IncomingMessageBuffer(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

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

    public Task<TransferDecision> WaitForDecisionAsync(
        Guid transferId,
        string conversationId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        var key = (transferId, conversationId);
        var completion = new TaskCompletionSource<TransferDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            if (_pendingTransferDecisions.TryGetValue(key, out var pendingDecision))
            {
                _pendingTransferDecisions.Remove(key);
                if (_timeProvider.GetUtcNow() - pendingDecision.ReceivedAt < PendingResponseLifetime)
                {
                    return Task.FromResult(pendingDecision.Value);
                }
            }

            _transferDecisions[key] = completion;
        }

        return WaitCoreAsync(key, completion, timeout, cancellationToken);
    }

    public Task<TransferOfferReceipt> WaitForOfferReceiptAsync(Guid transferId, string conversationId, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        var key = (transferId, conversationId);
        var completion = new TaskCompletionSource<TransferOfferReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            if (_pendingOfferReceipts.TryGetValue(key, out var pendingReceipt))
            {
                _pendingOfferReceipts.Remove(key);
                if (_timeProvider.GetUtcNow() - pendingReceipt.ReceivedAt < PendingResponseLifetime)
                {
                    return Task.FromResult(pendingReceipt.Value);
                }
            }

            _offerReceipts[key] = completion;
        }

        return WaitOfferReceiptCoreAsync(key, completion, timeout, cancellationToken);
    }

    private async Task<TransferDecision> WaitCoreAsync(
        (Guid TransferId, string ConversationId) key,
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
                _transferDecisions.Remove(key);
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
                TrackOfferReceipt((transferId, message.ConversationId), TransferOfferReceipt.Received);
                return;
            default:
                return;
        }

        var key = (transferId, message.ConversationId);
        lock (_sync)
        {
            if (_transferDecisions.TryGetValue(key, out var waiter))
            {
                waiter.TrySetResult(decision);
                return;
            }

            StorePending(_pendingTransferDecisions, key, decision);
        }
    }

    private async Task<TransferOfferReceipt> WaitOfferReceiptCoreAsync(
        (Guid TransferId, string ConversationId) key,
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
                _offerReceipts.Remove(key);
            }
        }
    }

    private void TrackOfferReceipt((Guid TransferId, string ConversationId) key, TransferOfferReceipt receipt)
    {
        lock (_sync)
        {
            if (_offerReceipts.TryGetValue(key, out var waiter))
            {
                waiter.TrySetResult(receipt);
                return;
            }

            StorePending(_pendingOfferReceipts, key, receipt);
        }
    }

    private void StorePending<T>(
        Dictionary<(Guid TransferId, string ConversationId), (T Value, DateTimeOffset ReceivedAt)> pending,
        (Guid TransferId, string ConversationId) key,
        T value)
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var (pendingKey, entry) in pending)
        {
            if (now - entry.ReceivedAt >= PendingResponseLifetime)
            {
                pending.Remove(pendingKey);
            }
        }

        if (!pending.ContainsKey(key) && pending.Count >= MaxPendingResponses)
        {
            var oldestKey = pending.MinBy(entry => entry.Value.ReceivedAt).Key;
            pending.Remove(oldestKey);
        }

        pending[key] = (value, now);
    }
}
