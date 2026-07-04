// Author: liaom
// SolutionName: Kitopia
// ProjectName: Core
// FileName:DeviceCommunication.cs
// Date: 2026/04/11 10:04
// FileEffect:

using Core.Services.DeviceCommunication.Discovery;
using Kitopia.DeviceCommunication.Identity;
using PluginCore;

namespace Core.Services.DeviceCommunication;

public class DeviceCommunication : IDeviceCommunication
{
    private readonly IDeviceIdentityStore _deviceIdentityStore;
    private readonly ILocalDataListener _localDataListener;
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;

    public DeviceCommunication(
        IDeviceIdentityStore deviceIdentityStore,
        ILocalDataListener localDataListener,
        IDeviceDiscoveryService deviceDiscoveryService)
    {
        _deviceIdentityStore = deviceIdentityStore;
        _localDataListener = localDataListener;
        _deviceDiscoveryService = deviceDiscoveryService;
    }

    public async Task StartAsync(CancellationToken token = default)
    {
        _deviceIdentityStore.EnsureIdentity();

        await _localDataListener.StartListeningAsync(token).ConfigureAwait(false);
        await _deviceDiscoveryService.StartAsync(token).ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        await _deviceDiscoveryService.StopAsync().ConfigureAwait(false);
        await _localDataListener.StopListeningAsync().ConfigureAwait(false);
    }
}
