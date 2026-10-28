namespace Kitopia.Desktop.Features.Search;

/// <summary>
/// Probes whether the Everything SDK can query its search engine.
/// </summary>
public interface IEverythingAvailabilityService
{
    bool IsAvailable(CancellationToken cancellationToken = default);
}
