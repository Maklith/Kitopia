using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Feature.Localization;
using Kitopia.Desktop.Abstractions.FileSystem;

namespace Kitopia.Desktop.Features.ViewModel.Windows;

public partial class FileLocksmithWindowViewModel : ObservableObject
{
    private readonly IFileLockService _fileLockService;
    private readonly Dictionary<string, bool> _expansionStateCache = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? _timer;
    private bool _isLoaded;

    [ObservableProperty]
    private ObservableCollection<FileLockInfo> _allRecords = [];

    [ObservableProperty]
    private ObservableCollection<FileLockInfo> _filteredRecords = [];

    public ObservableCollection<FileLockInfo> Processes
    {
        get => FilteredRecords;
        set => FilteredRecords = value;
    }

    [ObservableProperty]
    private ObservableCollection<FileLockTreeNodeViewModel> _treeNodes = [];

    [ObservableProperty]
    private FileLockTreeNodeViewModel? _selectedNode;

    [ObservableProperty]
    private bool _isTreeView = true;

    [ObservableProperty]
    private FileLockInfo? _selectedRecord;

    public bool HasSelection => SelectedNode != null || SelectedRecord != null;

    partial void OnSelectedNodeChanged(FileLockTreeNodeViewModel? value) => OnPropertyChanged(nameof(HasSelection));
    partial void OnSelectedRecordChanged(FileLockInfo? value) => OnPropertyChanged(nameof(HasSelection));

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _onlyLocked;

    [ObservableProperty]
    private bool _includeSubDirs = true;

    [ObservableProperty]
    private bool _autoRefresh = true;

    [ObservableProperty]
    private int _selectedIntervalIndex = 1; // 0 => 2s, 1 => 5s, 2 => 10s

    [ObservableProperty]
    private string? _rootDir;

    [ObservableProperty]
    private List<string>? _targetPaths;

    [ObservableProperty]
    private string _statusMessage = Lang.Get("lang.kitopia.ready");

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _scopeTitle = Lang.Get("lang.kitopia.system_wide_monitoring");

    public FileLocksmithWindowViewModel(IFileLockService fileLockService)
    {
        _fileLockService = fileLockService;
        SetupTimer();
    }

    private void SetupTimer()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(GetIntervalSeconds())
        };
        _timer.Tick += async (_, _) =>
        {
            if (AutoRefresh && !IsScanning && _isLoaded)
            {
                await ScanAsync();
            }
        };
        if (AutoRefresh)
        {
            _timer.Start();
        }
    }

    partial void OnSelectedIntervalIndexChanged(int value)
    {
        if (_timer != null)
        {
            _timer.Interval = TimeSpan.FromSeconds(GetIntervalSeconds());
        }
    }

    partial void OnAutoRefreshChanged(bool value)
    {
        if (value)
        {
            _timer?.Start();
        }
        else
        {
            _timer?.Stop();
        }
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();
    partial void OnOnlyLockedChanged(bool value) => ApplyFilter();

    partial void OnIncludeSubDirsChanged(bool value)
    {
        if (_isLoaded)
        {
            _ = ScanAsync();
        }
    }

    private int GetIntervalSeconds() => SelectedIntervalIndex switch
    {
        0 => 2,
        2 => 10,
        _ => 5
    };

    public void InitializeScope(string? rootDir, IReadOnlyCollection<string>? targetPaths)
    {
        _isLoaded = true;
        _expansionStateCache.Clear();
        RootDir = string.IsNullOrWhiteSpace(rootDir) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDir));
        TargetPaths = targetPaths?.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (!string.IsNullOrEmpty(RootDir))
        {
            ScopeTitle = Lang.Format("lang.kitopia.messages.monitoring_directory_value", RootDir);
        }
        else if (TargetPaths != null && TargetPaths.Count > 0)
        {
            ScopeTitle = TargetPaths.Count == 1 ? Lang.Format("lang.kitopia.messages.monitoring_file_value", TargetPaths[0]) : Lang.Format("lang.kitopia.messages.monitoring_value_files", TargetPaths.Count);
        }
        else
        {
            ScopeTitle = Lang.Get("lang.kitopia.system_wide_file_handle_monitoring");
        }

        _ = ScanAsync();
    }

    public void LoadProcesses(List<FileLockInfo> processes)
    {
        _isLoaded = true;
        _expansionStateCache.Clear();
        AllRecords = new ObservableCollection<FileLockInfo>(processes);
        if (TargetPaths == null && string.IsNullOrEmpty(RootDir))
        {
            TargetPaths = processes.Select(r => r.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        ApplyFilter();
        StatusMessage = Lang.Format("lang.kitopia.messages.value_process_records", processes.Count);
    }

    [RelayCommand]
    public async Task ScanAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
        StatusMessage = Lang.Get("lang.kitopia.scanning_file_handles_and_drivers");

        try
        {
            var sw = Stopwatch.StartNew();
            var results = await _fileLockService.ScanLocksAsync(
                rootDir: RootDir,
                includeSubDirs: IncludeSubDirs,
                targetPaths: TargetPaths);

            int lockedCount = results.Count(r => r.IsLocked);

            // Fast path: if scan results are identical to previous records, avoid rebuilding UI collections/tree
            if (_isLoaded && AllRecords.Count > 0 && AreRecordsEquivalent(results, AllRecords))
            {
                int currentShown = FilteredRecords.Count;
                StatusMessage = Lang.Format("lang.kitopia.messages.locked_value_showing_value_of_value_value_ms_last_refresh_value", lockedCount, currentShown, results.Count, sw.ElapsedMilliseconds, DateTime.Now);
                return;
            }

            AllRecords = new ObservableCollection<FileLockInfo>(results);
            ApplyFilter();

            int shownCount = FilteredRecords.Count;
            StatusMessage = Lang.Format("lang.kitopia.messages.locked_value_showing_value_of_value_value_ms_last_refresh_value", lockedCount, shownCount, results.Count, sw.ElapsedMilliseconds, DateTime.Now);
        }
        catch (Exception ex)
        {
            StatusMessage = Lang.Format("lang.kitopia.messages.scan_failed_value", ex.Message);
        }
        finally
        {
            IsScanning = false;
        }
    }

    private static bool AreRecordsEquivalent(IReadOnlyList<FileLockInfo> a, IReadOnlyList<FileLockInfo>? b)
    {
        if (b == null || a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            var x = a[i];
            var y = b[i];
            if (x.ProcessId != y.ProcessId ||
                x.IsLocked != y.IsLocked ||
                x.IsDriverModule != y.IsDriverModule ||
                !string.Equals(x.FilePath, y.FilePath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(x.ProcessName, y.ProcessName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(x.State, y.State, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    private void ApplyFilter()
    {
        var query = AllRecords.AsEnumerable();

        if (OnlyLocked)
        {
            query = query.Where(r => r.IsLocked);
        }

        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var ft = FilterText.Trim();
            query = query.Where(r =>
                r.FilePath.Contains(ft, StringComparison.OrdinalIgnoreCase) ||
                r.ProcessName.Contains(ft, StringComparison.OrdinalIgnoreCase) ||
                r.FileName.Contains(ft, StringComparison.OrdinalIgnoreCase) ||
                r.ProcessId.ToString().Contains(ft, StringComparison.OrdinalIgnoreCase));
        }

        FilteredRecords = new ObservableCollection<FileLockInfo>(query);
        BuildTree(FilteredRecords);
        OnPropertyChanged(nameof(Processes));
    }

    private void BuildTree(IEnumerable<FileLockInfo> records)
    {
        SaveExpansionStates(TreeNodes);
        string? selectedKey = SelectedNode?.NodeKey;

        var recordList = records.ToList();
        if (recordList.Count == 0)
        {
            TreeNodes = [];
            return;
        }

        var rootList = new List<FileLockTreeNodeViewModel>();
        var dirNodeDict = new Dictionary<string, FileLockTreeNodeViewModel>(StringComparer.OrdinalIgnoreCase);

        // Identify explicit target roots: from TargetPaths or RootDir
        var explicitTargetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explicitTargetDirs = new List<string>();

        if (TargetPaths != null && TargetPaths.Count > 0)
        {
            foreach (var p in TargetPaths)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                string norm = Path.TrimEndingDirectorySeparator(Path.GetFullPath(p));
                explicitTargetPaths.Add(norm);
                if (Directory.Exists(norm))
                {
                    explicitTargetDirs.Add(norm);
                }
            }
        }
        else if (!string.IsNullOrEmpty(RootDir))
        {
            string normRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(RootDir));
            explicitTargetPaths.Add(normRoot);
            explicitTargetDirs.Add(normRoot);
        }

        bool hasExplicitTargets = explicitTargetPaths.Count > 0;

        // Pre-create root directory nodes for explicit target directories so they appear at root
        foreach (var targetDir in explicitTargetDirs)
        {
            if (!dirNodeDict.TryGetValue(targetDir, out _))
            {
                bool isExp = true;
                if (_expansionStateCache.TryGetValue($"dir:{targetDir}", out bool cached))
                {
                    isExp = cached;
                }

                string folderName = Path.GetFileName(targetDir);
                if (string.IsNullOrEmpty(folderName)) folderName = targetDir;

                var dirNode = new FileLockTreeNodeViewModel
                {
                    NodeType = LockTreeNodeType.Directory,
                    Title = folderName,
                    Subtitle = targetDir,
                    FilePath = targetDir,
                    IsExpanded = isExp
                };
                RegisterNode(dirNode);
                dirNodeDict[targetDir] = dirNode;
                rootList.Add(dirNode);
            }
        }

        var pathGroups = recordList
            .GroupBy(r => r.FilePath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key)
            .ToList();

        foreach (var group in pathGroups)
        {
            string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(group.Key));
            bool isDirectory = Directory.Exists(fullPath);

            if (isDirectory)
            {
                var dirProcs = group.Where(r => r.ProcessId > 0).ToList();
                bool isSelfLocked = dirProcs.Count > 0;

                FileLockTreeNodeViewModel dirNode;
                if (explicitTargetPaths.Contains(fullPath))
                {
                    if (!dirNodeDict.TryGetValue(fullPath, out dirNode!))
                    {
                        bool isExp = true;
                        if (_expansionStateCache.TryGetValue($"dir:{fullPath}", out bool cached))
                        {
                            isExp = cached;
                        }

                        string folderName = Path.GetFileName(fullPath);
                        if (string.IsNullOrEmpty(folderName)) folderName = fullPath;

                        dirNode = new FileLockTreeNodeViewModel
                        {
                            NodeType = LockTreeNodeType.Directory,
                            Title = folderName,
                            Subtitle = fullPath,
                            FilePath = fullPath,
                            IsExpanded = isExp
                        };
                        RegisterNode(dirNode);
                        dirNodeDict[fullPath] = dirNode;
                        rootList.Add(dirNode);
                    }
                }
                else if (hasExplicitTargets)
                {
                    string? enclosingDir = FindEnclosingDirectory(fullPath, explicitTargetDirs);
                    if (enclosingDir != null)
                    {
                        dirNode = GetOrCreateDirectoryHierarchy(fullPath, enclosingDir, dirNodeDict, rootList);
                    }
                    else
                    {
                        dirNode = GetOrCreateDirectoryHierarchy(fullPath, fullPath, dirNodeDict, rootList);
                    }
                }
                else
                {
                    dirNode = GetOrCreateDirectoryHierarchy(fullPath, "", dirNodeDict, rootList);
                }

                if (isSelfLocked)
                {
                    dirNode.IsSelfLocked = true;
                    dirNode.SelfLockCount = dirProcs.Count;
                    dirNode.State = Lang.Get("lang.kitopia.locked");
                }

                foreach (var rec in dirProcs)
                {
                    var procNode = new FileLockTreeNodeViewModel
                    {
                        NodeType = LockTreeNodeType.Process,
                        Title = rec.ProcessName,
                        ProcessName = rec.ProcessName,
                        ProcessId = rec.ProcessId,
                        PidText = rec.PidText,
                        Subtitle = Lang.Get("lang.kitopia.locking_this_folder"),
                        FilePath = rec.FilePath,
                        State = Lang.Get("lang.kitopia.folder_locked"),
                        IsLocked = true,
                        SourceRecord = rec
                    };
                    RegisterNode(procNode);
                    dirNode.Children.Insert(0, procNode);
                }
            }
            else
            {
                string fileName = Path.GetFileName(fullPath);
                if (string.IsNullOrEmpty(fileName)) fileName = fullPath;

                bool isFileLocked = group.Any(r => r.IsLocked);
                bool isFileDriver = group.Any(r => r.IsDriverModule);
                string fileState = isFileLocked
                    ? (isFileDriver ? Lang.Get("lang.kitopia.driver_lock") : Lang.Get("lang.kitopia.locked"))
                    : Lang.Get("lang.kitopia.idle");

                string fileKey = $"file:{fullPath}";
                bool isFileExp = isFileLocked;
                if (_expansionStateCache.TryGetValue(fileKey, out bool cachedFile))
                {
                    isFileExp = cachedFile;
                }

                var fileNode = new FileLockTreeNodeViewModel
                {
                    NodeType = LockTreeNodeType.File,
                    Title = fileName,
                    Subtitle = Path.GetDirectoryName(fullPath) ?? "",
                    FilePath = fullPath,
                    State = fileState,
                    IsLocked = isFileLocked,
                    IsDriverModule = isFileDriver,
                    IsExpanded = isFileExp
                };
                RegisterNode(fileNode);

                foreach (var rec in group)
                {
                    if (rec.ProcessId > 0 || rec.IsDriverModule)
                    {
                        var procNode = new FileLockTreeNodeViewModel
                        {
                            NodeType = LockTreeNodeType.Process,
                            Title = rec.ProcessName,
                            ProcessName = rec.ProcessName,
                            ProcessId = rec.ProcessId,
                            PidText = rec.PidText,
                            Subtitle = rec.IsDriverModule ? Lang.Get("lang.kitopia.kernel_driver_service") : $"PID: {rec.ProcessId}",
                            FilePath = rec.FilePath,
                            State = rec.State,
                            IsLocked = rec.IsLocked,
                            IsDriverModule = rec.IsDriverModule,
                            SourceRecord = rec
                        };
                        RegisterNode(procNode);
                        fileNode.Children.Add(procNode);
                    }
                }

                if (explicitTargetPaths.Contains(fullPath))
                {
                    // Target file: Display directly on root!
                    rootList.Add(fileNode);
                }
                else if (hasExplicitTargets)
                {
                    string? enclosingDir = FindEnclosingDirectory(fullPath, explicitTargetDirs);
                    if (enclosingDir != null)
                    {
                        string parentDir = Path.GetDirectoryName(fullPath) ?? "";
                        if (string.Equals(parentDir, enclosingDir, StringComparison.OrdinalIgnoreCase))
                        {
                            dirNodeDict[enclosingDir].Children.Add(fileNode);
                        }
                        else if (!string.IsNullOrEmpty(parentDir))
                        {
                            var parentNode = GetOrCreateDirectoryHierarchy(parentDir, enclosingDir, dirNodeDict, rootList);
                            parentNode.Children.Add(fileNode);
                        }
                        else
                        {
                            dirNodeDict[enclosingDir].Children.Add(fileNode);
                        }
                    }
                    else
                    {
                        rootList.Add(fileNode);
                    }
                }
                else
                {
                    string parentDir = Path.GetDirectoryName(fullPath) ?? "";
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        var parentNode = GetOrCreateDirectoryHierarchy(parentDir, "", dirNodeDict, rootList);
                        parentNode.Children.Add(fileNode);
                    }
                    else
                    {
                        rootList.Add(fileNode);
                    }
                }
            }
        }

        foreach (var node in rootList)
        {
            UpdateFolderStats(node);
        }

        if (OnlyLocked)
        {
            rootList = PruneUnlockedNodes(rootList);
        }

        TreeNodes = new ObservableCollection<FileLockTreeNodeViewModel>(rootList);

        if (!string.IsNullOrEmpty(selectedKey))
        {
            SelectedNode = FindNodeByKey(rootList, selectedKey);
        }
    }

    private static string? FindEnclosingDirectory(string path, IEnumerable<string> targetDirs)
    {
        string? bestMatch = null;
        foreach (var dir in targetDirs)
        {
            if (path.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(dir + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                if (bestMatch == null || dir.Length > bestMatch.Length)
                {
                    bestMatch = dir;
                }
            }
        }
        return bestMatch;
    }

    private void SaveExpansionStates(IEnumerable<FileLockTreeNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            _expansionStateCache[node.NodeKey] = node.IsExpanded;
            if (node.Children.Count > 0)
            {
                SaveExpansionStates(node.Children);
            }
        }
    }

    private void RegisterNode(FileLockTreeNodeViewModel node)
    {
        node.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FileLockTreeNodeViewModel.IsExpanded))
            {
                _expansionStateCache[node.NodeKey] = node.IsExpanded;
            }
        };
    }

    private static FileLockTreeNodeViewModel? FindNodeByKey(IEnumerable<FileLockTreeNodeViewModel> nodes, string key)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.NodeKey, key, StringComparison.OrdinalIgnoreCase))
                return node;
            var found = FindNodeByKey(node.Children, key);
            if (found != null) return found;
        }
        return null;
    }

    private FileLockTreeNodeViewModel GetOrCreateDirectoryHierarchy(
        string dirPath,
        string rootDir,
        Dictionary<string, FileLockTreeNodeViewModel> cache,
        List<FileLockTreeNodeViewModel> rootList)
    {
        string normDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dirPath));
        if (cache.TryGetValue(normDir, out var existing))
        {
            return existing;
        }

        string folderName = Path.GetFileName(normDir);
        if (string.IsNullOrEmpty(folderName)) folderName = normDir;

        string nodeKey = $"dir:{normDir}";
        bool isExp = true;
        if (_expansionStateCache.TryGetValue(nodeKey, out bool cached))
        {
            isExp = cached;
        }

        var node = new FileLockTreeNodeViewModel
        {
            NodeType = LockTreeNodeType.Directory,
            Title = folderName,
            Subtitle = normDir,
            FilePath = normDir,
            IsExpanded = isExp
        };
        RegisterNode(node);
        cache[normDir] = node;

        if (!string.IsNullOrEmpty(rootDir) && normDir.StartsWith(rootDir, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(normDir, rootDir, StringComparison.OrdinalIgnoreCase))
            {
                if (!rootList.Contains(node)) rootList.Add(node);
                return node;
            }

            string parentDir = Path.GetDirectoryName(normDir) ?? "";
            if (!string.IsNullOrEmpty(parentDir))
            {
                var parentNode = GetOrCreateDirectoryHierarchy(parentDir, rootDir, cache, rootList);
                parentNode.Children.Add(node);
            }
            else
            {
                rootList.Add(node);
            }
        }
        else
        {
            string parentDir = Path.GetDirectoryName(normDir) ?? "";
            if (!string.IsNullOrEmpty(parentDir) && !string.Equals(parentDir, normDir, StringComparison.OrdinalIgnoreCase))
            {
                var parentNode = GetOrCreateDirectoryHierarchy(parentDir, rootDir, cache, rootList);
                parentNode.Children.Add(node);
            }
            else
            {
                rootList.Add(node);
            }
        }

        return node;
    }

    private int UpdateFolderStats(FileLockTreeNodeViewModel node)
    {
        if (node.IsProcess)
        {
            return 0;
        }

        if (node.IsFile)
        {
            return node.IsLocked ? 1 : 0;
        }

        int childLocks = 0;
        foreach (var child in node.Children)
        {
            if (child.IsFile)
            {
                if (child.IsLocked) childLocks++;
            }
            else if (child.IsDirectory)
            {
                int subLocks = UpdateFolderStats(child);
                childLocks += (child.IsSelfLocked ? 1 : 0) + subLocks;
            }
        }

        node.ChildLockCount = childLocks;
        if (!_expansionStateCache.ContainsKey(node.NodeKey))
        {
            node.IsExpanded = node.IsSelfLocked || node.HasChildLocks;
            _expansionStateCache[node.NodeKey] = node.IsExpanded;
        }

        return childLocks;
    }

    private static List<FileLockTreeNodeViewModel> PruneUnlockedNodes(List<FileLockTreeNodeViewModel> nodes)
    {
        var result = new List<FileLockTreeNodeViewModel>();
        foreach (var node in nodes)
        {
            if (node.IsDirectory)
            {
                var filteredChildren = PruneUnlockedNodes(node.Children.ToList());
                node.Children.Clear();
                foreach (var c in filteredChildren) node.Children.Add(c);

                if (node.IsSelfLocked || node.HasChildLocks || node.Children.Count > 0)
                {
                    result.Add(node);
                }
            }
            else if (node.IsFile)
            {
                if (node.IsLocked)
                {
                    result.Add(node);
                }
            }
            else
            {
                result.Add(node);
            }
        }
        return result;
    }

    public void SetMonitoredFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;
        _expansionStateCache.Clear();
        RootDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
        TargetPaths = null;
        ScopeTitle = Lang.Format("lang.kitopia.messages.monitoring_directory_value", RootDir);
        _ = ScanAsync();
    }

    [RelayCommand]
    public void ExpandAll()
    {
        foreach (var node in TreeNodes)
        {
            node.ExpandAll();
        }
        SaveExpansionStates(TreeNodes);
    }

    [RelayCommand]
    public void CollapseAll()
    {
        foreach (var node in TreeNodes)
        {
            node.CollapseAll();
        }
        SaveExpansionStates(TreeNodes);
    }

    [RelayCommand]
    public async Task KillSelectedAsync(object? parameter = null)
    {
        if (parameter is FileLockTreeNodeViewModel treeNode)
        {
            await KillNodeAsync(treeNode);
            return;
        }

        var target = parameter as FileLockInfo ?? SelectedRecord;
        if (target == null && SelectedNode != null)
        {
            await KillNodeAsync(SelectedNode);
            return;
        }

        if (target == null) return;

        if (target.IsDriverModule)
        {
            var err = await _fileLockService.StopDriverForFileAsync(target.FilePath);
            StatusMessage = err == null ? Lang.Format("lang.kitopia.messages.stopped_driver_value", target.FilePath) : err;
            await ScanAsync();
            return;
        }

        if (target.ProcessId > 4)
        {
            var success = await _fileLockService.UnlockFileAsync([target.ProcessId]);
            StatusMessage = success ? Lang.Format("lang.kitopia.messages.terminated_process_pid_value", target.ProcessId) : Lang.Format("lang.kitopia.messages.some_processes_could_not_be_terminated_for_pid_value", target.ProcessId);
            await ScanAsync();
        }
    }

    [RelayCommand]
    public async Task KillNodeAsync(FileLockTreeNodeViewModel? node)
    {
        var target = node ?? SelectedNode;
        if (target == null) return;

        if (target.IsProcess)
        {
            if (target.IsDriverModule)
            {
                var err = await _fileLockService.StopDriverForFileAsync(target.FilePath);
                StatusMessage = err == null ? Lang.Format("lang.kitopia.messages.stopped_driver_value", target.FilePath) : err;
            }
            else if (target.ProcessId > 4)
            {
                var success = await _fileLockService.UnlockFileAsync([target.ProcessId]);
                StatusMessage = success ? Lang.Format("lang.kitopia.messages.terminated_process_pid_value", target.ProcessId) : Lang.Format("lang.kitopia.messages.unable_to_terminate_process_pid_value", target.ProcessId);
            }
            await ScanAsync();
            return;
        }

        if (target.IsFile)
        {
            await KillAllForFileAsync(target.FilePath);
            return;
        }

        if (target.IsDirectory)
        {
            await KillAllForDirectoryAsync(target);
        }
    }

    [RelayCommand]
    public async Task KillAllForFileAsync(object? parameter = null)
    {
        string? filePath = null;

        if (parameter is string s)
        {
            filePath = s;
        }
        else if (parameter is FileLockTreeNodeViewModel node)
        {
            filePath = node.FilePath;
        }
        else if (parameter is FileLockInfo info)
        {
            filePath = info.FilePath;
        }
        else if (SelectedNode != null && !string.IsNullOrEmpty(SelectedNode.FilePath))
        {
            filePath = SelectedNode.FilePath;
        }
        else if (SelectedRecord != null && !string.IsNullOrEmpty(SelectedRecord.FilePath))
        {
            filePath = SelectedRecord.FilePath;
        }

        if (string.IsNullOrEmpty(filePath)) return;

        var matching = AllRecords
            .Where(r => string.Equals(r.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matching.Any(r => r.IsDriverModule))
        {
            await _fileLockService.StopDriverForFileAsync(filePath);
        }

        var pids = matching
            .Where(r => !r.IsDriverModule && r.ProcessId > 4)
            .Select(r => r.ProcessId)
            .Distinct()
            .ToList();

        if (pids.Count > 0)
        {
            await _fileLockService.UnlockFileAsync(pids);
        }

        StatusMessage = Lang.Format("lang.kitopia.messages.attempted_to_release_all_file_locks_value", Path.GetFileName(filePath));
        await ScanAsync();
    }

    private async Task KillAllForDirectoryAsync(FileLockTreeNodeViewModel dirNode)
    {
        var filePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectFilePaths(dirNode, filePaths);

        if (filePaths.Count == 0) return;

        var matching = AllRecords
            .Where(r => filePaths.Contains(r.FilePath))
            .ToList();

        foreach (var drv in matching.Where(r => r.IsDriverModule))
        {
            await _fileLockService.StopDriverForFileAsync(drv.FilePath);
        }

        var pids = matching
            .Where(r => !r.IsDriverModule && r.ProcessId > 4)
            .Select(r => r.ProcessId)
            .Distinct()
            .ToList();

        if (pids.Count > 0)
        {
            await _fileLockService.UnlockFileAsync(pids);
        }

        StatusMessage = Lang.Format("lang.kitopia.messages.attempted_to_release_all_locks_in_directory_value", dirNode.Title);
        await ScanAsync();
    }

    private static void CollectFilePaths(FileLockTreeNodeViewModel node, HashSet<string> set)
    {
        if (node.IsFile && !string.IsNullOrEmpty(node.FilePath))
        {
            set.Add(node.FilePath);
        }
        foreach (var child in node.Children)
        {
            CollectFilePaths(child, set);
        }
    }

    [RelayCommand]
    public async Task UnlockAndDeleteAsync(object? parameter = null)
    {
        string? filePath = null;

        if (parameter is string s)
        {
            filePath = s;
        }
        else if (parameter is FileLockTreeNodeViewModel node)
        {
            filePath = node.FilePath;
        }
        else if (parameter is FileLockInfo info)
        {
            filePath = info.FilePath;
        }
        else if (SelectedNode != null && !string.IsNullOrEmpty(SelectedNode.FilePath))
        {
            filePath = SelectedNode.FilePath;
        }
        else if (SelectedRecord != null && !string.IsNullOrEmpty(SelectedRecord.FilePath))
        {
            filePath = SelectedRecord.FilePath;
        }

        if (string.IsNullOrEmpty(filePath)) return;

        var err = await _fileLockService.UnlockAndDeleteFileAsync(filePath);
        if (err == null)
        {
            StatusMessage = Lang.Format("lang.kitopia.messages.unlocked_and_deleted_value", Path.GetFileName(filePath));
        }
        else
        {
            StatusMessage = Lang.Format("lang.kitopia.messages.delete_failed_value", err);
        }
        await ScanAsync();
    }

    [RelayCommand]
    public void OpenFileLocation(object? parameter = null)
    {
        string? filePath = null;

        if (parameter is string s)
        {
            filePath = s;
        }
        else if (parameter is FileLockTreeNodeViewModel node)
        {
            filePath = node.FilePath;
        }
        else if (parameter is FileLockInfo info)
        {
            filePath = info.FilePath;
        }
        else if (SelectedNode != null && !string.IsNullOrEmpty(SelectedNode.FilePath))
        {
            filePath = SelectedNode.FilePath;
        }
        else if (SelectedRecord != null && !string.IsNullOrEmpty(SelectedRecord.FilePath))
        {
            filePath = SelectedRecord.FilePath;
        }

        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            if (Directory.Exists(filePath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{filePath}\"")
                {
                    UseShellExecute = true
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"")
                {
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            StatusMessage = Lang.Format("lang.kitopia.messages.unable_to_open_location_value", ex.Message);
        }
    }

    [RelayCommand]
    public async Task Unlock(FileLockInfo? processInfo)
    {
        await KillSelectedAsync(processInfo);
    }

    [RelayCommand]
    public async Task UnlockAll()
    {
        var pids = FilteredRecords
            .Where(r => r.ProcessId > 4)
            .Select(p => p.ProcessId)
            .Distinct()
            .ToList();

        if (pids.Count > 0)
        {
            await _fileLockService.UnlockFileAsync(pids);
            await ScanAsync();
        }
    }

    public void OnWindowClosing()
    {
        _timer?.Stop();
        _timer = null;
    }
}
