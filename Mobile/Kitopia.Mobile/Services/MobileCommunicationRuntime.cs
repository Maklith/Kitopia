using Kitopia.Feature.DeviceCommunication.Transport;
using Kitopia.Feature.DeviceCommunication.Diagnostics;

namespace Kitopia.Mobile.Services;

public sealed class MobileCommunicationRuntime : IMobileCommunicationRuntime
{
    private const string LogCategory = "MobileRuntime";
    private readonly ILocalDataListener _localDataListener;

    public MobileCommunicationRuntime(ILocalDataListener localDataListener)
    {
        _localDataListener = localDataListener;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        DeviceCommunicationDiagnostics.Info(LogCategory, "Starting local data listener.");
        await _localDataListener.StartListeningAsync(cancellationToken);
        DeviceCommunicationDiagnostics.Info(
            LogCategory,
            $"Local data listener started. TcpPort={_localDataListener.TcpPort}.");
    }

    public async Task StopAsync()
    {
        DeviceCommunicationDiagnostics.Info(LogCategory, "Stopping local data listener.");
        await _localDataListener.StopListeningAsync();
        DeviceCommunicationDiagnostics.Info(LogCategory, "Local data listener stopped.");
    }
}
