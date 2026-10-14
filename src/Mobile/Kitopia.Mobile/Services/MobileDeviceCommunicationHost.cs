using Kitopia.Feature.DeviceCommunication.Discovery;

namespace Kitopia.Mobile.Services;

public sealed class MobileDeviceCommunicationHost
{
    private readonly IMobileCommunicationRuntime _runtime;
    private readonly IDeviceDiscoveryService _discoveryService;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private bool _started;

    public MobileDeviceCommunicationHost(IMobileCommunicationRuntime runtime, IDeviceDiscoveryService discoveryService)
    {
        _runtime = runtime;
        _discoveryService = discoveryService;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (_started)
            {
                return;
            }

            var runtimeStartAttempted = false;
            var discoveryStartAttempted = false;
            try
            {
                runtimeStartAttempted = true;
                await _runtime.StartAsync(cancellationToken);
                discoveryStartAttempted = true;
                await _discoveryService.StartAsync(cancellationToken);
                _started = true;
            }
            catch
            {
                if (discoveryStartAttempted)
                {
                    await TryStopAsync(_discoveryService.StopAsync);
                }

                if (runtimeStartAttempted)
                {
                    await TryStopAsync(_runtime.StopAsync);
                }

                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            if (!_started)
            {
                return;
            }

            Exception? stopError = null;
            try
            {
                await _discoveryService.StopAsync();
            }
            catch (Exception exception)
            {
                stopError = exception;
            }

            try
            {
                await _runtime.StopAsync();
            }
            catch (Exception exception)
            {
                stopError = stopError is null
                    ? exception
                    : new AggregateException(stopError, exception);
            }

            if (stopError is not null)
            {
                throw stopError;
            }

            _started = false;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void SetBackgroundMode(bool background)
    {
        _discoveryService.SetBackgroundMode(background);
    }

    private static async Task TryStopAsync(Func<Task> stopAsync)
    {
        try
        {
            await stopAsync();
        }
        catch
        {
        }
    }
}
