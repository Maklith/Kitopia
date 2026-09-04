namespace Kitopia.Desktop.Abstractions.FileSystem;

public sealed class FileLockInfo
{
    public int ProcessId { get; set; }

    public string ProcessName { get; set; } = string.Empty;

    public string ExecutablePath { get; set; } = string.Empty;

    public DateTime StartTime { get; set; }

    public string User { get; set; } = string.Empty;

    public List<string> LockedFiles { get; set; } = [];

    public string FilePath { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public bool IsDriverModule { get; set; }

    public bool IsLocked => State == "已锁定" || State == "驱动锁定";

    public string PidText => ProcessId <= 0 ? "-" : ProcessId.ToString();

    public string FileName => string.IsNullOrEmpty(FilePath) ? string.Empty : Path.GetFileName(FilePath);
}
