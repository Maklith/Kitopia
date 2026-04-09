using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Core.Services.DeviceCommunication.Requests;

public sealed class RequestTracker : IRequestTracker
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pending = new();

    public async Task<RequestDecision> WaitForBooleanResponseAsync(
        string requestId,
        Func<Task> sendRequest,
        TimeSpan timeout,
        string duplicateError)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw new InvalidOperationException("请求标识不能为空。");
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(requestId, tcs))
        {
            throw new InvalidOperationException(duplicateError);
        }

        try
        {
            await sendRequest();
            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
            if (completedTask != tcs.Task)
            {
                return RequestDecision.TimedOut;
            }

            return tcs.Task.Result ? RequestDecision.Accepted : RequestDecision.Rejected;
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    public void Resolve(string requestId, bool accepted)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        if (_pending.TryGetValue(requestId, out var tcs))
        {
            tcs.TrySetResult(accepted);
        }
    }

    public void CancelAll()
    {
        foreach (var request in _pending.Values)
        {
            request.TrySetResult(false);
        }

        _pending.Clear();
    }
}
