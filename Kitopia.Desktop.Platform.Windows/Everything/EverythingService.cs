using Kitopia.Desktop.Features.Search;

namespace Kitopia.Desktop.Platform.Windows.Everything;

public sealed class EverythingService : IEverythingService
{
    public bool IsRun()
    {
        return EverythingTools.IsRun();
    }
}
