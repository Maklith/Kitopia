// Author: liaom
// SolutionName: Kitopia
// ProjectName: Core
// FileName:IDeviceCommunication.cs
// Date: 2026/04/11 10:04
// FileEffect:

namespace Core.Services.DeviceCommunication;

public interface IDeviceCommunication {
    public Task StartAsync(CancellationToken token=default);
    public Task StopAsync();
}