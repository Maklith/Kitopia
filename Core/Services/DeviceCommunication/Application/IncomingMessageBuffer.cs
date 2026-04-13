using System.Threading.Channels;
using Core.Services.DeviceCommunication.Messages;
using Core.Services.DeviceCommunication.Messages.Chat;

namespace Core.Services.DeviceCommunication.Application;

public enum TransferDecision
{
    Accepted = 1,
    Rejected = 2,
    Timeout = 3
}

public sealed class IncomingMessageBuffer : IIncomingMessageSink
{
    private readonly Channel<IncomingMessageEvent> _channel = Channel.CreateBounded<IncomingMessageEvent>(1024);
    private readonly object _sync = new();
    private readonly Dictionary<Guid, TaskCompletionSource<TransferDecision>> _transferDecisions = new();
    private readonly Dictionary<Guid, TransferDecision> _pendingTransferDecisions = new();

    public ValueTask PublishAsync(AppMessage message, CancellationToken cancellationToken = default)
    {
        TrackTransferDecision(message);
        return _channel.Writer.WriteAsync(new IncomingMessageEvent(message), cancellationToken);
    }

    public ValueTask PublishEventAsync(IncomingMessageEvent messageEvent, CancellationToken cancellationToken = default)
    {
        TrackTransferDecision(messageEvent.Message);
        return _channel.Writer.WriteAsync(messageEvent, cancellationToken);
    }

    public IAsyncEnumerable<IncomingMessageEvent> ReceiveAsync(CancellationToken cancellationToken = default)
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
}
