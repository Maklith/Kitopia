using System.Diagnostics;
using Kitopia.Desktop.Abstractions.FileSystem;

namespace Kitopia.Desktop.Platform.Linux;

public sealed class LinuxFileLockService : IFileLockService
{
    private const string ProcRoot = "/proc";
    private const string DeletedFileSuffix = " (deleted)";

    public Task<List<FileLockInfo>> CheckFileLocksAsync(
        string[] filePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        if (filePaths.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("File paths cannot contain null or whitespace values.", nameof(filePaths));
        }

        var normalizedPaths = filePaths
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return Task.Run(
            () => FindProcessesHoldingFiles(normalizedPaths, cancellationToken),
            cancellationToken);
    }

    public Task<bool> UnlockFileAsync(
        List<int> processIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(processIds);
        if (processIds.Any(processId => processId <= 0))
        {
            throw new ArgumentException("Process IDs must be positive.", nameof(processIds));
        }

        return Task.Run(() =>
        {
            var allSucceeded = true;
            foreach (var processId in processIds.Distinct())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (processId == Environment.ProcessId)
                {
                    allSucceeded = false;
                    continue;
                }

                try
                {
                    using var process = Process.GetProcessById(processId);
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                                                   System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    allSucceeded = false;
                }
            }

            return allSucceeded;
        }, cancellationToken);
    }

    private static List<FileLockInfo> FindProcessesHoldingFiles(
        string[] normalizedPaths,
        CancellationToken cancellationToken)
    {
        var results = new List<FileLockInfo>();
        if (normalizedPaths.Length == 0 || !Directory.Exists(ProcRoot))
        {
            return results;
        }

        var requestedPaths = normalizedPaths.ToHashSet(StringComparer.Ordinal);
        string[] processDirectories;
        try
        {
            processDirectories = Directory.GetDirectories(ProcRoot);
        }
        catch (IOException)
        {
            return results;
        }
        catch (UnauthorizedAccessException)
        {
            return results;
        }

        foreach (var processDirectory in processDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!int.TryParse(Path.GetFileName(processDirectory), out var processId))
            {
                continue;
            }

            var matchingFiles = FindMatchingFileDescriptors(processDirectory, requestedPaths, cancellationToken);
            if (matchingFiles.Count == 0)
            {
                continue;
            }

            results.Add(CreateFileLockInfo(processDirectory, processId, matchingFiles));
        }

        return results;
    }

    private static List<string> FindMatchingFileDescriptors(
        string processDirectory,
        HashSet<string> requestedPaths,
        CancellationToken cancellationToken)
    {
        string[] descriptors;
        try
        {
            descriptors = Directory.GetFileSystemEntries(Path.Combine(processDirectory, "fd"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var matches = new HashSet<string>(StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = TryReadLinkTarget(descriptor);
            if (target is null)
            {
                continue;
            }

            var normalizedTarget = NormalizeLinkTarget(target);
            if (normalizedTarget is not null && requestedPaths.Contains(normalizedTarget))
            {
                matches.Add(normalizedTarget);
            }
        }

        return matches.OrderBy(path => path, StringComparer.Ordinal).ToList();
    }

    private static FileLockInfo CreateFileLockInfo(
        string processDirectory,
        int processId,
        List<string> matchingFiles)
    {
        var result = new FileLockInfo
        {
            ProcessId = processId,
            LockedFiles = matchingFiles,
            ExecutablePath = NormalizeLinkTarget(TryReadLinkTarget(Path.Combine(processDirectory, "exe"))) ??
                             string.Empty,
            User = ResolveUser(processDirectory)
        };

        try
        {
            using var process = Process.GetProcessById(processId);
            result.ProcessName = process.ProcessName;
            result.StartTime = process.StartTime;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                                           System.ComponentModel.Win32Exception or NotSupportedException)
        {
            result.ProcessName = TryReadFirstLine(Path.Combine(processDirectory, "comm")) ?? string.Empty;
        }

        return result;
    }

    private static string ResolveUser(string processDirectory)
    {
        try
        {
            var uidLine = File.ReadLines(Path.Combine(processDirectory, "status"))
                .FirstOrDefault(line => line.StartsWith("Uid:", StringComparison.Ordinal));
            var uid = uidLine?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
            if (uid is null)
            {
                return string.Empty;
            }

            foreach (var line in File.ReadLines("/etc/passwd"))
            {
                var fields = line.Split(':');
                if (fields.Length > 2 && string.Equals(fields[2], uid, StringComparison.Ordinal))
                {
                    return fields[0];
                }
            }

            return uid;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static string? TryReadLinkTarget(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           NotSupportedException)
        {
            return null;
        }
    }

    private static string? NormalizeLinkTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        if (target.EndsWith(DeletedFileSuffix, StringComparison.Ordinal))
        {
            target = target[..^DeletedFileSuffix.Length];
        }

        if (!Path.IsPathRooted(target))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(target);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string? TryReadFirstLine(string path)
    {
        try
        {
            return File.ReadLines(path).FirstOrDefault()?.Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
