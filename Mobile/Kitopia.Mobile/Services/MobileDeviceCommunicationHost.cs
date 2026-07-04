using Kitopia.DeviceCommunication.Discovery;

namespace Kitopia.Mobile.Services;

public sealed class MobileDeviceCommunicationHost
{
    private readonly IMobileCommunicationRuntime _runtime;
    private readonly IDeviceDiscoveryService _discoveryService;
    private bool _started;

    public MobileDeviceCommunicationHost(IMobileCommunicationRuntime runtime, IDeviceDiscoveryService discoveryService)
    {
        _runtime = runtime;
        _discoveryService = discoveryService;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        await _runtime.StartAsync(cancellationToken);
        await _discoveryService.StartAsync(cancellationToken);
    }

    public async Task StopAsync()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        await _discoveryService.StopAsync();
        await _runtime.StopAsync();
    }
}
