namespace Core.Services.Interfaces;

public interface IFileLocksmith
{
    // Check processes locking the files
    Task<List<LockingProcessInfo>> CheckFileLocksAsync(string[] filePaths);
    
    // Unlock files by terminating processes
    Task<bool> UnlockFileAsync(List<int> processIds);
}

public class LockingProcessInfo
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; }
    public string ExecutablePath { get; set; }
    public DateTime StartTime { get; set; }
    public string User { get; set; }
    public List<string> LockedFiles { get; set; } = new();
}
