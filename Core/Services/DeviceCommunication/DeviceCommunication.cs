// Author: liaom
// SolutionName: Kitopia
// ProjectName: Core
// FileName:DeviceCommunication.cs
// Date: 2026/04/11 10:04
// FileEffect:

using Core.Services.Config;
using Core.Services.DeviceCommunication.Discovery;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Core.Services.DeviceCommunication;

public class DeviceCommunication : IDeviceCommunication
{
    public async Task StartAsync(CancellationToken token = default)
    {
        if (ConfigManger.Config.EnsureDeviceIdentity())
        {
            ConfigManger.Save("KitopiaConfig");
        }

        await ServiceManager.Services.GetService<ILocalDataListener>()!.StartListeningAsync(token);
        await ServiceManager.Services.GetService<IDeviceDiscoveryService>()!.StartAsync(token);
    }

    public async Task StopAsync()
    {
        await ServiceManager.Services.GetService<ILocalDataListener>()!.StopListeningAsync();
        await ServiceManager.Services.GetService<IDeviceDiscoveryService>()!.StopAsync();
    }
}
