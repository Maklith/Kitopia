using Kitopia.Desktop.Features.Search;

namespace Kitopia.Desktop.Platform.Windows.Everything;

public sealed class EverythingAvailabilityService : IEverythingAvailabilityService
{
    public bool IsAvailable(CancellationToken cancellationToken = default)
    {
        return EverythingClient.IsAvailable(cancellationToken);
    }
}
