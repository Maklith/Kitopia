using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Threading;
using Kitopia.Desktop.Features.Services.Config;
using PluginCore;
using Serilog;

namespace Kitopia.Desktop.Features.Search.ProcessUsage;

/// <summary>
/// Samples CPU and working-set usage for the applications listed in the search results.
/// CPU is reported against the whole machine the way Task Manager does it: a process's
/// processor-time delta is divided by the elapsed wall clock and the logical processor count,
/// so multi-threaded applications stay within 0-100%.
/// </summary>
public sealed class ProcessUsageService : IProcessUsageService, IDisposable
{
    private const int MinimumRefreshMilliseconds = 500;
    private const int MaximumRefreshMilliseconds = 5000;

    private static readonly ILogger Logger =
        Kitopia.Desktop.Features.Services.LogManager.Logger.ForContext<ProcessUsageService>();

    private readonly ConcurrentDictionary<string, ProcessUsageSnapshot> _snapshots =
        new(StringComparer.OrdinalIgnoreCase);

    // Keyed by process id; the start time guards against a recycled id. Only the sampling loop
    // touches this dictionary, so it needs no lock.
    private readonly Dictionary<int, ProcessBaseline> _baselines = new();
    private readonly Func<Process, (long StartTimeTicks, TimeSpan ProcessorTime)?> _readProcessTimes;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _lock = new();
    private Task? _loop;
    private string[] _targets = [];
    private bool _enabled;

    // Set when the caller toggles sampling, consumed by the sampling loop.
    private bool _resetBaselines;

    // Bumped whenever the caller clears state so an in-flight pass cannot publish stale readings.
    private int _generation;

    public event Action? Updated;

    public ProcessUsageService(Func<Process, (long StartTimeTicks, TimeSpan ProcessorTime)?>? readProcessTimes = null)
    {
        _readProcessTimes = readProcessTimes ?? ReadProcessTimes;
    }

    public bool Enabled
    {
        get
        {
            lock (_lock)
            {
                return _enabled;
            }
        }
        set
        {
            lock (_lock)
            {
                if (_enabled == value) return;
                _enabled = value;
                _resetBaselines = true;
                _generation++;

                // The loop stops sampling while disabled, so dropping the last readings here keeps
                // TryGet from replaying stale values when the window is shown again.
                _snapshots.Clear();
            }

            if (value) EnsureLoop();
        }
    }

    public void SetTargets(IEnumerable<string> processNames)
    {
        var targets = processNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => Path.GetFileNameWithoutExtension(name))
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        lock (_lock)
        {
            if (_targets.AsSpan().SequenceEqual(targets, StringComparer.OrdinalIgnoreCase)) return;
            _targets = targets;

            foreach (var key in _snapshots.Keys)
            {
                if (!targets.Contains(key, StringComparer.OrdinalIgnoreCase)) _snapshots.TryRemove(key, out _);
            }
        }
    }

    public bool TryGet(string processName, out ProcessUsageSnapshot snapshot)
    {
        snapshot = ProcessUsageSnapshot.Empty;
        return !string.IsNullOrWhiteSpace(processName) && _snapshots.TryGetValue(processName, out snapshot);
    }

    /// <summary>
    /// Maps a search result to the process name reported by the operating system. Only results
    /// whose key is an executable path qualify: Windows Installer shortcuts keep the .lnk path as
    /// their key and packaged applications keep an application user model id, so neither can be
    /// resolved to a process name here.
    /// </summary>
    public static bool TryResolveProcessName(SearchViewItem? item, out string processName)
    {
        processName = string.Empty;
        if (item is null || item.FileType is not (FileType.应用程序 or FileType.文件)) return false;

        var path = item.OnlyKey;
        if (string.IsNullOrWhiteSpace(path) ||
            !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            path.Contains('!'))
        {
            return false;
        }

        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(name)) return false;

        processName = name;
        return true;
    }

    /// <summary>Formats a sampled CPU percentage for the search result subtitle.</summary>
    public static string FormatCpu(double? percent) =>
        percent is { } value ? value.ToString("0.0") + "%" : "--";

    /// <summary>Formats a working-set size for the search result subtitle.</summary>
    public static string FormatMemory(long bytes)
    {
        const double megabyte = 1024d * 1024d;
        const double gigabyte = megabyte * 1024d;

        return bytes >= gigabyte
            ? (bytes / gigabyte).ToString("0.0") + " GB"
            : (Math.Max(bytes, 0L) / megabyte).ToString("0") + " MB";
    }

    private void EnsureLoop()
    {
        lock (_lock)
        {
            if (_loop is { IsCompleted: false }) return;
            _loop = Task.Run(() => RunAsync(_lifetime.Token));
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval());
        var stopwatch = Stopwatch.StartNew();
        var lastTimestamp = stopwatch.ElapsedTicks;

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                // Pick up a changed refresh interval so the setting applies without a restart.
                var interval = RefreshInterval();
                if (timer.Period != interval) timer.Period = interval;

                if (!Enabled)
                {
                    // Keep the reference point current so a hidden window does not produce a spike.
                    lastTimestamp = stopwatch.ElapsedTicks;
                    continue;
                }

                try
                {
                    Sample(ref lastTimestamp, stopwatch);
                }
                catch (Exception exception)
                {
                    Logger.Warning(exception, "Sampling application resource usage failed.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    private static TimeSpan RefreshInterval()
    {
        var milliseconds = ConfigManger.Config?.processUsageRefreshMilliseconds ?? 1000;
        return TimeSpan.FromMilliseconds(
            Math.Clamp(milliseconds, MinimumRefreshMilliseconds, MaximumRefreshMilliseconds));
    }

    private void Sample(ref long lastTimestamp, Stopwatch stopwatch)
    {
        string[] targets;
        bool reset;
        int generation;
        lock (_lock)
        {
            targets = _targets;
            reset = _resetBaselines;
            _resetBaselines = false;
            generation = _generation;
        }

        // The loop owns the baseline table, so resets requested by Enabled are applied here.
        if (reset) _baselines.Clear();

        // One clock for the whole pass so every process shares the same interval.
        var now = stopwatch.ElapsedTicks;
        var elapsed = (now - lastTimestamp) / (double)Stopwatch.Frequency;
        lastTimestamp = now;

        if (targets.Length == 0)
        {
            if (_snapshots.IsEmpty) return;
            _snapshots.Clear();
            NotifyUpdated();
            return;
        }

        var cores = Math.Max(1, Environment.ProcessorCount);
        var cpu = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var memory = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var alive = new HashSet<int>();

        foreach (var name in targets)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Logger.Debug(exception, "Enumerating processes named {ProcessName} failed.", name);
                continue;
            }

            foreach (var process in processes)
            {
                try
                {
                    alive.Add(process.Id);
                    memory[name] = memory.GetValueOrDefault(name) + process.WorkingSet64;

                    var times = _readProcessTimes(process);
                    if (times is not { } reading)
                    {
                        _baselines.Remove(process.Id);
                        continue;
                    }
                    var startTime = reading.StartTimeTicks;
                    var processorTime = reading.ProcessorTime;
                    var isKnown = _baselines.TryGetValue(process.Id, out var baseline)
                                  && baseline.StartTime == startTime;
                    _baselines[process.Id] = new ProcessBaseline(startTime, processorTime);

                    // A newly seen process has no interval behind it yet; CPU arrives next pass.
                    if (!isKnown || elapsed <= 0d) continue;

                    var delta = (processorTime - baseline.ProcessorTime).TotalSeconds;
                    // Zero is a valid idle reading, not a missing sample.
                    if (delta < 0d) continue;

                    cpu[name] = cpu.GetValueOrDefault(name) + delta / elapsed / cores * 100d;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // Do not carry a CPU interval across an unreadable or exited process.
                    _baselines.Remove(process.Id);
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        foreach (var id in _baselines.Keys.Where(id => !alive.Contains(id)).ToList())
        {
            _baselines.Remove(id);
        }

        // Publishing is skipped when Enabled toggled mid-pass; the caller already cleared the results
        // this pass was computed against, so they must not be written back.
        var changed = false;
        lock (_lock)
        {
            if (_generation != generation || !_enabled) return;

            foreach (var (name, bytes) in memory)
            {
                var snapshot = new ProcessUsageSnapshot(
                    name,
                    cpu.TryGetValue(name, out var percent) ? Math.Clamp(percent, 0d, 100d) : null,
                    bytes);

                if (_snapshots.TryGetValue(name, out var previous) && previous == snapshot) continue;
                _snapshots[name] = snapshot;
                changed = true;
            }

            foreach (var name in _snapshots.Keys.Where(key => !memory.ContainsKey(key)).ToList())
            {
                _snapshots.TryRemove(name, out _);
                changed = true;
            }
        }

        if (changed) NotifyUpdated();
    }

    private static (long StartTimeTicks, TimeSpan ProcessorTime)? ReadProcessTimes(Process process)
    {
        try
        {
            return (process.StartTime.ToUniversalTime().Ticks, process.TotalProcessorTime);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private void NotifyUpdated()
    {
        var handler = Updated;
        if (handler is null) return;

        if (Dispatcher.UIThread.CheckAccess())
            handler();
        else
            Dispatcher.UIThread.Post(handler);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _enabled = false;
        }

        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private readonly record struct ProcessBaseline(long StartTime, TimeSpan ProcessorTime);
}
