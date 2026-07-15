namespace Kitopia.Desktop.Abstractions.FileSystem;

public sealed class FileLockInfo
{
    public int ProcessId { get; set; }

    public string ProcessName { get; set; } = string.Empty;

    public string ExecutablePath { get; set; } = string.Empty;

    public DateTime StartTime { get; set; }

    public string User { get; set; } = string.Empty;

    public List<string> LockedFiles { get; set; } = [];
}
