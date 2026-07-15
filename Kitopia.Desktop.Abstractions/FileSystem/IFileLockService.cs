namespace Kitopia.Desktop.Abstractions.FileSystem;

public interface IFileLockService
{
    Task<List<FileLockInfo>> CheckFileLocksAsync(
        string[] filePaths,
        CancellationToken cancellationToken = default);

    Task<bool> UnlockFileAsync(
        List<int> processIds,
        CancellationToken cancellationToken = default);
}
