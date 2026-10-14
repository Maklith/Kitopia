namespace Kitopia.Desktop.Abstractions.FileSystem;

public interface IFileLockService
{
    Task<List<FileLockInfo>> CheckFileLocksAsync(
        string[] filePaths,
        CancellationToken cancellationToken = default);

    Task<bool> UnlockFileAsync(
        List<int> processIds,
        CancellationToken cancellationToken = default);

    Task<List<FileLockInfo>> ScanLocksAsync(
        string? rootDir = null,
        bool includeSubDirs = true,
        IReadOnlyCollection<string>? targetPaths = null,
        CancellationToken cancellationToken = default)
    {
        return CheckFileLocksAsync(targetPaths?.ToArray() ?? [], cancellationToken);
    }

    Task<string?> StopDriverForFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>("当前平台不支持驱动服务停止。");
    }

    Task<string?> UnlockAndDeleteFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>("当前平台不支持解除占用并删除。");
    }
}
