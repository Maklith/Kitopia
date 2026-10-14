using Kitopia.Feature.DeviceCommunication.Discovery;
using Kitopia.Feature.DeviceCommunication.Identity;
using Kitopia.Feature.DeviceCommunication.Transport;

namespace Kitopia.Feature.DeviceCommunication;

public interface IDeviceCommunicationRuntime
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}

public sealed class DeviceCommunicationRuntime : IDeviceCommunicationRuntime
{
    private readonly IDeviceIdentityStore _deviceIdentityStore;
    private readonly ILocalDataListener _localDataListener;
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private bool _started;

    public DeviceCommunicationRuntime(
        IDeviceIdentityStore deviceIdentityStore,
        ILocalDataListener localDataListener,
        IDeviceDiscoveryService deviceDiscoveryService)
    {
        _deviceIdentityStore = deviceIdentityStore;
        _localDataListener = localDataListener;
        _deviceDiscoveryService = deviceDiscoveryService;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_started)
            {
                return;
            }

            _deviceIdentityStore.EnsureIdentity();
            var listenerStartAttempted = false;
            var discoveryStartAttempted = false;
            try
            {
                listenerStartAttempted = true;
                await _localDataListener.StartListeningAsync(cancellationToken).ConfigureAwait(false);
                discoveryStartAttempted = true;
                await _deviceDiscoveryService.StartAsync(cancellationToken).ConfigureAwait(false);
                _started = true;
            }
            catch
            {
                if (discoveryStartAttempted)
                {
                    await TryStopAsync(_deviceDiscoveryService.StopAsync).ConfigureAwait(false);
                }

                if (listenerStartAttempted)
                {
                    await TryStopAsync(_localDataListener.StopListeningAsync).ConfigureAwait(false);
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
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_started)
            {
                return;
            }

            Exception? stopError = null;
            try
            {
                await _deviceDiscoveryService.StopAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                stopError = exception;
            }

            try
            {
                await _localDataListener.StopListeningAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                stopError = stopError is null
                    ? exception
                    : new AggregateException(stopError, exception);
            }

            _started = false;
            if (stopError is not null)
            {
                throw stopError;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private static async Task TryStopAsync(Func<Task> stopAsync)
    {
        try
        {
            await stopAsync().ConfigureAwait(false);
        }
        catch
        {
        }
    }
}
