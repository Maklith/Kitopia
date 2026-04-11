// Author: liaom
// SolutionName: Kitopia
// ProjectName: Core
// FileName:ILocalDataListener.cs
// Date: 2026/04/11 11:04
// FileEffect:

namespace Core.Services.DeviceCommunication;

public interface ILocalDataListener {
    public int UdpPort { get; }
    public int QuicPort { get; }
    public bool SupportsQuic { get; }
    public Task StartListeningAsync(CancellationToken token=default);
    public Task StopListeningAsync();
}