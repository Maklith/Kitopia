using Kitopia.Desktop.Features.Search;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Services.Plugin;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.SearchWindow.InputData;
using PluginCore.SearchWindow.InputDataAnalyzer;
using Serilog;

namespace Kitopia.Desktop.Features.Indexing;

/// <summary>
/// Refreshes application and plug-in supplied entries without giving an index to a UI view model.
/// </summary>
public sealed class IndexMaintenanceService : IIndexMaintenanceService
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<IndexMaintenanceService>();
    private static readonly EnumerationOptions ManagedFileEnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    };
    private readonly IIndexService _index;
    private readonly IAppToolService _appTools;
    private readonly Dictionary<object, List<string>> _analyzerIndexedKeys = new();
    private readonly SemaphoreSlim _everythingRefreshGate = new(1, 1);
    private int _reloading;
    private int _backgroundIndexing;

    public IndexMaintenanceService(IIndexService index, IAppToolService appTools)
    {
        _index = index;
        _appTools = appTools;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Keep first-use search responsive: applications and pinyin are ready before the
        // potentially long Everything, document, image, and OCR passes are queued.
        await Task.Run(ReloadApplications, cancellationToken);
        await Task.Run(() => RefreshWindowOpenEntries(rebuildPinyin: false), cancellationToken);
        await RefreshManagedFilesAsync(cancellationToken);
        QueueBackgroundIndexing();
    }

    public async Task RefreshEverythingFilesAsync(CancellationToken cancellationToken = default)
    {
        await _everythingRefreshGate.WaitAsync(cancellationToken);
        try
        {
            if (!ConfigManger.Config.useEverything || !OperatingSystem.IsWindows())
            {
                await SynchronizeEverythingFilesAsync([], cancellationToken);
                return;
            }

            var everything = ServiceManager.Services.GetService<IEverythingService>();
            if (everything is null)
            {
                UpdateDiscoveryStatus("Everything service is unavailable; keeping the previous managed file manifest.");
                return;
            }

            if (!everything.IsRun())
            {
                StartEverythingWhenConfigured();
                for (var attempt = 0; attempt < 12 && !everything.IsRun(); attempt++)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                }

                if (!everything.IsRun())
                {
                    UpdateDiscoveryStatus("Everything is not running; managed file discovery is waiting for it to start.");
                    return;
                }
            }

            try
            {
                await SynchronizeEverythingFilesAsync(
                    EnumerateFilteredEverythingFiles(cancellationToken),
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Logger.Warning(exception, "Everything file discovery failed.");
                UpdateDiscoveryStatus(exception.Message);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            _everythingRefreshGate.Release();
        }
    }

    private async Task SynchronizeEverythingFilesAsync(IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        if (await _index.SynchronizeFilesAsync(paths, IndexSource.EverythingManaged, cancellationToken))
        {
            await _index.RebuildPinyinSearcherAsync(cancellationToken);
        }
    }

    public async Task RefreshManagedFilesAsync(CancellationToken cancellationToken = default)
    {
        var config = ConfigManger.Config;
        try
        {
            if (await _index.SynchronizeFilesAsync(
                    EnumerateManagedFiles(config, cancellationToken),
                    IndexSource.Manual,
                    cancellationToken))
            {
                await _index.RebuildPinyinSearcherAsync(cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.Warning(exception, "Managed file discovery failed; keeping the previous manifest.");
        }
    }

    private IEnumerable<string> EnumerateFilteredEverythingFiles(CancellationToken cancellationToken)
    {
        foreach (var path in _appTools.EnumerateEverythingIndexedFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (IndexService.ShouldAutomaticallyIndexFile(fullPath))
            {
                yield return fullPath;
            }
        }
    }

    private IEnumerable<string> EnumerateManagedFiles(KitopiaConfig config, CancellationToken cancellationToken)
    {
        var roots = config.managedIndexDirectories
            .Concat(DefaultIndexDirectories())
            .Distinct(PathComparer);
        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(root, "*", ManagedFileEnumerationOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IndexService.ShouldAutomaticallyIndexFile(path)
                    && TryNormalizePath(path, out var normalizedPath))
                {
                    yield return normalizedPath;
                }
            }
        }

        foreach (var file in config.managedIndexFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(file) && TryNormalizePath(file, out var normalizedPath))
            {
                yield return normalizedPath;
            }
        }
    }

    private static bool TryNormalizePath(string path, out string normalizedPath)
    {
        try
        {
            normalizedPath = Path.GetFullPath(path);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            normalizedPath = string.Empty;
            return false;
        }
    }

    private static IEnumerable<string> DefaultIndexDirectories()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    }

    public void RefreshWindowOpenEntries() => RefreshWindowOpenEntries(rebuildPinyin: true);

    private void RefreshWindowOpenEntries(bool rebuildPinyin)
    {
        var changed = false;
        foreach (var (_, analyzers) in PluginOverall.SearchWindowInputDataAnalyzers)
        foreach (var analyzerTuple in analyzers)
        {
            if ((analyzerTuple.Item1() & InputDataAnalyzeTimeFlags.WindowOpenUpdateIndex) == 0)
            {
                continue;
            }

            var entries = new List<SearchEntry>();
            foreach (var item in analyzerTuple.Item2([]))
            {
                if (string.IsNullOrWhiteSpace(item.OnlyKey) || string.IsNullOrWhiteSpace(item.ItemDisplayName))
                {
                    continue;
                }

                entries.Add(new SearchEntry
                {
                    DisplayName = item.ItemDisplayName,
                    OnlyKey = item.OnlyKey,
                    FileType = item.FileType,
                    IconSymbol = item.IconSymbol,
                    Arguments = item.Arguments,
                    LaunchPath = item.LaunchPath,
                    IconPath = item.IconPath,
                    StartDirectory = item.StartDirectory
                });
            }

            changed |= SynchronizeAnalyzerEntries(analyzerTuple, entries);
        }

        if (changed && rebuildPinyin)
        {
            _index.RebuildPinyinSearcher();
        }
    }

    private bool SynchronizeAnalyzerEntries(object analyzer, IReadOnlyList<SearchEntry> entries)
    {
        var incoming = entries
            .GroupBy(entry => entry.OnlyKey, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToDictionary(entry => entry.OnlyKey, entry => entry, StringComparer.Ordinal);
        var changed = false;
        if (_analyzerIndexedKeys.TryGetValue(analyzer, out var oldKeys))
        {
            foreach (var key in oldKeys.Where(key => !incoming.ContainsKey(key)))
            {
                changed |= _index.TryRemove(key);
            }
        }

        foreach (var entry in incoming.Values)
        {
            if (_index.TryGetValue(entry.OnlyKey, out var existing)
                && existing.Equals(entry))
            {
                continue;
            }

            changed |= _index.TryAdd(entry, IndexSource.Plugin);
        }

        _analyzerIndexedKeys[analyzer] = incoming.Keys.ToList();
        return changed;
    }

    private void ReloadApplications()
    {
        if (Interlocked.Exchange(ref _reloading, 1) != 0)
        {
            return;
        }

        try
        {
            _appTools.CleanupInvalidItems(_index);
            _appTools.IndexAllApps(_index, logging: false, ConfigManger.Config.useEverything);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Application index refresh failed.");
        }
        finally
        {
            Volatile.Write(ref _reloading, 0);
        }
    }

    private void QueueBackgroundIndexing()
    {
        if (Interlocked.Exchange(ref _backgroundIndexing, 1) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await RefreshEverythingFilesAsync();
                await _index.IndexIncrementalAsync(IndexRebuildScope.Files);
            }
            catch (Exception exception)
            {
                Logger.Warning(exception, "Background document and image indexing failed.");
            }
            finally
            {
                Volatile.Write(ref _backgroundIndexing, 0);
            }
        });
    }

    private void StartEverythingWhenConfigured()
    {
        if (!ConfigManger.Config.useEverything || !OperatingSystem.IsWindows())
        {
            return;
        }

        var everything = ServiceManager.Services.GetService<IEverythingService>();
        if (everything is not null && !everything.IsRun())
        {
            _appTools.AutoStartEverything(_index, () => { });
        }
    }

    private void UpdateDiscoveryStatus(string message)
    {
        Logger.Information("{Message}", message);
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

}
