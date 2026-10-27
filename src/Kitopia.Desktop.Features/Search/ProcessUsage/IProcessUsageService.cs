namespace Kitopia.Desktop.Features.Search.ProcessUsage;

/// <summary>
/// Sample source for the resource usage shown next to running applications in the search window.
/// </summary>
public interface IProcessUsageService
{
    /// <summary>
    /// Enables sampling. The search window sets this while it is visible so an idle launcher
    /// never walks the process list.
    /// </summary>
    bool Enabled { get; set; }

    /// <summary>Raised on the UI thread after every sample.</summary>
    event Action? Updated;

    /// <summary>Limits sampling to the process names currently listed in the search results.</summary>
    void SetTargets(IEnumerable<string> processNames);

    /// <summary>Reads the latest sample for one process name.</summary>
    bool TryGet(string processName, out ProcessUsageSnapshot snapshot);
}
