namespace Kitopia.Mobile.Services;

public interface IMobileCommunicationRuntime
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}
