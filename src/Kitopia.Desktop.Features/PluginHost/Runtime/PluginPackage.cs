using NuGet.Versioning;
using Serilog;

namespace Kitopia.Desktop.Features.Services.Plugin;

// Owns an extracted package and its rollback directory until activation succeeds.
internal sealed class PluginPackage : IDisposable
{
    private readonly string _stagingDirectory;
    private string? _targetDirectory;
    private string? _backupDirectory;
    private bool _completed;

    public PluginLocalInfo Info { get; }

    public PluginPackage(string stagingDirectory, string expectedSign, string expectedVersion)
    {
        _stagingDirectory = stagingDirectory;
        Info = PluginDiscoveryService.ReadPlugin(stagingDirectory);
        if (Info.ToPlgString() != expectedSign ||
            !NuGetVersion.TryParse(expectedVersion, out var requestedVersion) ||
            !VersionComparer.VersionRelease.Equals(NuGetVersion.Parse(Info.PluginBaseInfo.Version), requestedVersion))
            throw new InvalidDataException("插件包的标识或版本与下载请求不一致。");
    }

    public void Activate(string targetDirectory)
    {
        if (_targetDirectory is not null) throw new InvalidOperationException("插件包已经切换。");
        targetDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetDirectory));
        if (Directory.Exists(targetDirectory))
        {
            var backupDirectory = Path.Combine(Path.GetDirectoryName(targetDirectory)!,
                $".backup-{Path.GetFileName(targetDirectory)}-{Guid.NewGuid():N}");
            Directory.Move(targetDirectory, backupDirectory);
            _backupDirectory = backupDirectory;
        }
        try
        {
            Directory.Move(_stagingDirectory, targetDirectory);
            _targetDirectory = targetDirectory;
        }
        catch
        {
            if (_backupDirectory is not null) Directory.Move(_backupDirectory, targetDirectory);
            _backupDirectory = null;
            throw;
        }
    }

    public void Complete()
    {
        _completed = true;
        if (_backupDirectory is null) return;
        try { Directory.Delete(_backupDirectory, true); }
        catch (IOException exception) { Log.Warning(exception, "旧插件目录清理失败：{Path}", _backupDirectory); }
        catch (UnauthorizedAccessException exception) { Log.Warning(exception, "旧插件目录清理失败：{Path}", _backupDirectory); }
    }

    public void Dispose()
    {
        if (!_completed && _targetDirectory is not null)
        {
            // Retain the backup if the OS prevents rollback; never delete the only old version.
            try { Directory.Move(_targetDirectory, _stagingDirectory); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                File.WriteAllText(Path.Combine(_targetDirectory, _backupDirectory is null ? ".remove" : ".rollback"),
                    _backupDirectory is null ? string.Empty : Path.GetFileName(_backupDirectory));
                // Startup now owns recovery. A later Dispose must not move the restored old version.
                _completed = true;
                throw;
            }
            if (_backupDirectory is not null) Directory.Move(_backupDirectory, _targetDirectory);
            _targetDirectory = null;
        }
        if (Directory.Exists(_stagingDirectory)) Directory.Delete(_stagingDirectory, true);
    }
}
