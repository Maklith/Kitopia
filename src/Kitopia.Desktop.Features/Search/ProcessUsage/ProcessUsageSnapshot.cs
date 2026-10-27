namespace Kitopia.Desktop.Features.Search.ProcessUsage;

/// <summary>
/// One application's aggregated resource usage. Entries are keyed by process name so
/// multi-process applications such as browsers report a single combined value. A null
/// CPU value means the first sample for that process is still pending.
/// </summary>
public readonly record struct ProcessUsageSnapshot(string ProcessName, double? CpuPercent, long WorkingSetBytes)
{
    public static readonly ProcessUsageSnapshot Empty = new(string.Empty, null, 0L);
}
