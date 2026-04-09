using System;
using System.Threading.Tasks;

namespace Core.Services.DeviceCommunication.Requests;

public interface IRequestTracker
{
    Task<RequestDecision> WaitForBooleanResponseAsync(
        string requestId,
        Func<Task> sendRequest,
        TimeSpan timeout,
        string duplicateError);

    void Resolve(string requestId, bool accepted);
    void CancelAll();
}
