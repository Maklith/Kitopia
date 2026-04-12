using System.Threading.Channels;
using Core.Services.DeviceCommunication.Messages;
using Core.Services.DeviceCommunication.Messages.Chat;

namespace Core.Services.DeviceCommunication.Application;

public sealed class IncomingMessageBuffer : IIncomingMessageSink
{
    private readonly Channel<IncomingMessageEvent> _channel = Channel.CreateBounded<IncomingMessageEvent>(1024);
    private readonly object _sync = new();
    private readonly Dictionary<Guid, TaskCompletionSource<bool>> _transferDecisions = new();

    public ValueTask PublishAsync(AppMessage message, CancellationToken cancellationToken = default)
    {
        TrackTransferDecision(message);
        return _channel.Writer.WriteAsync(new IncomingMessageEvent(message), cancellationToken);
    }

    public ValueTask PublishEventAsync(IncomingMessageEvent messageEvent, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(messageEvent, cancellationToken);
    }

    public IAsyncEnumerable<IncomingMessageEvent> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }

    public Task<bool> WaitForDecisionAsync(Guid transferId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            _transferDecisions[transferId] = completion;
        }

        return WaitCoreAsync(transferId, completion, timeout, cancellationToken);
    }

    private async Task<bool> WaitCoreAsync(
        Guid transferId,
        TaskCompletionSource<bool> completion,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            var completed = await Task.WhenAny(completion.Task, Task.Delay(timeout, cancellationToken));
            if (completed != completion.Task)
            {
                return false;
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
        bool accepted;

        switch (message)
        {
            case FileAcceptChatMessage fileAccept:
                transferId = fileAccept.TransferId;
                accepted = true;
                break;
            case FileRejectChatMessage fileReject:
                transferId = fileReject.TransferId;
                accepted = false;
                break;
            default:
                return;
        }

        lock (_sync)
        {
            if (_transferDecisions.TryGetValue(transferId, out var waiter))
            {
                waiter.TrySetResult(accepted);
            }
        }
    }
}
