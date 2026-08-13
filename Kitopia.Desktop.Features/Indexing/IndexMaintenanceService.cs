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
            var everythingPaths = new HashSet<string>(PathComparer);
            if (!ConfigManger.Config.useEverything || !OperatingSystem.IsWindows())
            {
                await SynchronizeEverythingFilesAsync(everythingPaths, cancellationToken);
                return;
            }

            var everything = ServiceManager.Services.GetService<IEverythingService>();
            if (everything is null)
            {
                await SynchronizeEverythingFilesAsync(everythingPaths, cancellationToken);
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
                    await SynchronizeEverythingFilesAsync(everythingPaths, cancellationToken);
                    return;
                }
            }

            try
            {
                await Task.Run(() => _appTools.VisitEverythingIndexedFiles(path =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(path)) return;

                    try
                    {
                        var fullPath = Path.GetFullPath(path);
                        if (!IndexService.ShouldAutomaticallyIndexFile(fullPath))
                        {
                            return;
                        }

                        everythingPaths.Add(fullPath);
                    }
                    catch (Exception exception) when (exception is ArgumentException
                                                       or NotSupportedException
                                                       or PathTooLongException)
                    {
                    }
                }), cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Logger.Warning(exception, "Everything file discovery failed.");
                UpdateDiscoveryStatus(exception.Message);
                await SynchronizeEverythingFilesAsync(everythingPaths, cancellationToken);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await SynchronizeEverythingFilesAsync(everythingPaths, cancellationToken);
        }
        finally
        {
            _everythingRefreshGate.Release();
        }
    }

    private async Task SynchronizeEverythingFilesAsync(HashSet<string> paths, CancellationToken cancellationToken)
    {
        if (_index.SynchronizeFiles(paths, IndexSource.EverythingManaged))
        {
            await _index.RebuildPinyinSearcherAsync(cancellationToken);
        }
    }

    public async Task RefreshManagedFilesAsync(CancellationToken cancellationToken = default)
    {
        var paths = new HashSet<string>(PathComparer);
        var config = ConfigManger.Config;
        var roots = config.managedIndexDirectories
            .Concat(DefaultIndexDirectories())
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    if (IndexService.ShouldAutomaticallyIndexFile(path)) paths.Add(Path.GetFullPath(path));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Logger.Debug(exception, "Managed directory scan skipped {Directory}.", root);
            }
        }

        foreach (var file in config.managedIndexFiles)
        {
            if (File.Exists(file)) paths.Add(Path.GetFullPath(file));
        }

        if (_index.SynchronizeFiles(paths, IndexSource.Manual))
        {
            await _index.RebuildPinyinSearcherAsync(cancellationToken);
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
                await _index.IndexIncrementalAsync(IndexRebuildScope.Documents);
                await _index.IndexIncrementalAsync(IndexRebuildScope.Images);
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
