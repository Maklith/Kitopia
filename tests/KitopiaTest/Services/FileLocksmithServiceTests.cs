using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Kitopia.Desktop.Abstractions.FileSystem;
using Kitopia.Desktop.Features.ViewModel.Windows;
using Kitopia.Desktop.Platform.Windows.Services;
using Kitopia.Feature.Localization;

namespace KitopiaTest.Services;

[TestClass]
public sealed class FileLocksmithServiceTests
{
    [TestMethod]
    public async Task ScanLocksAsync_DirectoryWithInaccessibleProcessHandles_ResolvesOwnerUsingRestartManager()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            Assert.Inconclusive("This regression requires a standard token so SeDebugPrivilege cannot bypass the process ACL.");
        }

        var directory = Path.Combine(Path.GetTempPath(), $"kitopia_restricted_lock_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "locked.zip");
        var ready = Path.Combine(directory, "ready");
        File.WriteAllText(file, "lock fixture");
        using var child = Process.Start(new ProcessStartInfo("powershell.exe")
        {
            Arguments = $"-NoProfile -NonInteractive -Command \"$f = [System.IO.File]::Open('{file.Replace("'", "''")}', 'Open', 'ReadWrite', 'None'); [System.IO.File]::WriteAllText('{ready.Replace("'", "''")}', 'ready'); Start-Sleep -Seconds 30\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
        Assert.IsNotNull(child);

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!File.Exists(ready))
            {
                Assert.IsFalse(child.HasExited, "The lock fixture exited before opening its file.");
                await Task.Delay(50, timeout.Token);
            }

            // Deny PROCESS_DUP_HANDLE while retaining query and termination access.
            var descriptor = new RawSecurityDescriptor("D:(D;;0x0040;;;WD)(A;;GA;;;WD)");
            var security = new byte[descriptor.BinaryLength];
            descriptor.GetBinaryForm(security, 0);
            Assert.IsTrue(SetKernelObjectSecurity(child.Handle, 4, security),
                $"Unable to restrict fixture handles: {Marshal.GetLastWin32Error()}");

            IFileLockService service = new FileLocksmithService();
            var records = await service.ScanLocksAsync(rootDir: directory);
            Assert.IsTrue(records.Any(r => r.FilePath == file && r.ProcessId == child.Id && r.IsLocked),
                "Directory scanning must resolve the owner even when its file handles cannot be duplicated.");
            Assert.IsFalse(records.Any(r => r.FilePath == file && r.ProcessId == 0),
                "A resolved lock must not retain the unknown-owner placeholder.");
        }
        finally
        {
            if (!child.HasExited) child.Kill();
            await child.WaitForExitAsync();
            File.Delete(ready);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    File.Delete(file);
                    break;
                }
                catch (IOException) when (attempt < 20)
                {
                    // Other handle scans can still hold a temporary duplicate of the fixture's handle.
                    await Task.Delay(100);
                }
            }
            Directory.Delete(directory);
        }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKernelObjectSecurity(IntPtr handle, uint securityInformation, byte[] securityDescriptor);

    [TestMethod]
    [DataRow("File", true, true)]
    [DataRow("Selected", true, true)]
    [DataRow("All", true, true)]
    [DataRow("File", false, true)]
    [DataRow("File", true, false)]
    public async Task UnlockCommands_UnknownOwner_ResolveBeforeTerminationAndKeepResult(
        string action, bool identifyOwner, bool terminateSuccessfully)
    {
        var path = Path.Combine(Path.GetTempPath(), "kitopia_unknown_lock.zip");
        var unknown = new FileLockInfo { FilePath = path, State = "已锁定" };
        var service = new FileLockServiceStub
        {
            Records = [unknown],
            ResolvedRecords = identifyOwner
                ? [new FileLockInfo { FilePath = path, ProcessId = 7340, State = "已锁定" }]
                : [unknown],
            UnlockSucceeds = terminateSuccessfully
        };
        var vm = new FileLocksmithWindowViewModel(service) { AutoRefresh = false };
        try
        {
            vm.LoadProcesses([unknown]);
            switch (action)
            {
                case "File":
                    await vm.KillNodeCommand.ExecuteAsync(new FileLockTreeNodeViewModel
                    {
                        NodeType = LockTreeNodeType.File, FilePath = path, IsLocked = true
                    });
                    break;
                case "Selected":
                    vm.SelectedRecord = unknown;
                    await vm.KillSelectedCommand.ExecuteAsync(null);
                    break;
                case "All":
                    await vm.UnlockAllCommand.ExecuteAsync(null);
                    break;
            }

            CollectionAssert.AreEqual(new[] { path }, service.CheckedPaths);
            CollectionAssert.AreEqual(identifyOwner ? new[] { 7340 } : [], service.TerminatedProcessIds);
            var key = identifyOwner && terminateSuccessfully
                ? "lang.kitopia.messages.released_locks_value"
                : "lang.kitopia.messages.unable_to_release_locks_value";
            Assert.AreEqual(Lang.Format(key, action == "All" ? vm.ScopeTitle : path), vm.StatusMessage,
                "The post-operation scan must not overwrite the operation result.");
        }
        finally
        {
            vm.OnWindowClosing();
        }
    }

    [TestMethod]
    public async Task KillNodeAsync_Directory_IncludesItsOwnLocksAndChildLocks()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kitopia_directory_lock");
        var file = Path.Combine(directory, "locked.zip");
        var service = new FileLockServiceStub();
        var vm = new FileLocksmithWindowViewModel(service) { AutoRefresh = false };
        try
        {
            vm.LoadProcesses([
                new FileLockInfo { FilePath = directory, ProcessId = 1000, State = "已锁定" },
                new FileLockInfo { FilePath = file, ProcessId = 2000, State = "已锁定" }
            ]);
            var node = new FileLockTreeNodeViewModel { NodeType = LockTreeNodeType.Directory, FilePath = directory };
            node.Children.Add(new FileLockTreeNodeViewModel { NodeType = LockTreeNodeType.File, FilePath = file });

            await vm.KillNodeCommand.ExecuteAsync(node);

            CollectionAssert.AreEquivalent(new[] { 1000, 2000 }, service.TerminatedProcessIds);
        }
        finally
        {
            vm.OnWindowClosing();
        }
    }

    private sealed class FileLockServiceStub : IFileLockService
    {
        public List<FileLockInfo> Records { get; init; } = [];
        public List<FileLockInfo> ResolvedRecords { get; init; } = [];
        public bool UnlockSucceeds { get; init; } = true;
        public string[] CheckedPaths { get; private set; } = [];
        public List<int> TerminatedProcessIds { get; private set; } = [];
        public Func<Task<List<FileLockInfo>>>? Scan { get; init; }
        public Action<string>? Delete { get; init; }
        public string? DeleteError { get; init; }

        public Task<List<FileLockInfo>> CheckFileLocksAsync(string[] filePaths, CancellationToken cancellationToken = default)
        {
            CheckedPaths = filePaths;
            return Task.FromResult(ResolvedRecords);
        }

        public Task<bool> UnlockFileAsync(List<int> processIds, CancellationToken cancellationToken = default)
        {
            TerminatedProcessIds = processIds;
            return Task.FromResult(UnlockSucceeds);
        }

        public Task<List<FileLockInfo>> ScanLocksAsync(string? rootDir = null, bool includeSubDirs = true,
            IReadOnlyCollection<string>? targetPaths = null, CancellationToken cancellationToken = default)
            => Scan?.Invoke() ?? Task.FromResult(Records);

        public Task<string?> UnlockAndDeleteFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            Delete?.Invoke(filePath);
            return Task.FromResult(DeleteError);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnlockAndDeleteCommand_ExistingFile_RemovesRowAndAllowsRecreatedFile(bool monitorDirectory)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"kitopia_deleted_row_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "delete.txt");
        File.WriteAllText(file, "delete fixture");
        IFileLockService service = new FileLocksmithService();
        var vm = new FileLocksmithWindowViewModel(service) { AutoRefresh = false };
        try
        {
            if (monitorDirectory) vm.RootDir = directory;
            else vm.TargetPaths = [file];
            vm.LoadProcesses(await service.CheckFileLocksAsync([file]));
            Assert.IsTrue(vm.AllRecords.Any(r => r.FilePath == file));

            await vm.UnlockAndDeleteCommand.ExecuteAsync(file);

            Assert.IsFalse(File.Exists(file));
            Assert.IsFalse(vm.AllRecords.Any(r => r.FilePath == file), "Deleted files must leave the monitor records.");
            Assert.IsFalse(vm.FilteredRecords.Any(r => r.FilePath == file), "Deleted files must leave the table.");
            Assert.IsTrue(vm.TreeNodes.All(n => n.FilePath != file && n.Children.All(c => c.FilePath != file)),
                "Deleted files must leave the tree.");

            File.WriteAllText(file, "recreated fixture");
            await vm.ScanAsync();
            Assert.IsTrue(vm.AllRecords.Any(r => r.FilePath == file), "Monitoring the path must continue after recreation.");
        }
        finally
        {
            vm.OnWindowClosing();
            File.Delete(file);
            Directory.Delete(directory);
        }
    }

    [TestMethod]
    public async Task UnlockAndDeleteCommand_ScanInProgress_DiscardsOldSnapshotAndRefreshesAfterDeletion()
    {
        var file = Path.Combine(Path.GetTempPath(), $"kitopia_delete_race_{Guid.NewGuid():N}.txt");
        File.WriteAllText(file, "delete fixture");
        var deletedRecord = new FileLockInfo { FilePath = file, State = "空闲" };
        var survivor = new FileLockInfo { FilePath = file + ".other", State = "空闲" };
        var oldSnapshot = new TaskCompletionSource<List<FileLockInfo>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var scans = 0;
        var service = new FileLockServiceStub
        {
            Delete = File.Delete,
            Scan = () => ++scans == 1 ? oldSnapshot.Task : Task.FromResult(new List<FileLockInfo> { survivor })
        };
        var vm = new FileLocksmithWindowViewModel(service) { AutoRefresh = false };
        Task? scan = null;
        try
        {
            vm.LoadProcesses([deletedRecord, survivor]);
            vm.SelectedRecord = deletedRecord;
            vm.SelectedNode = vm.TreeNodes.Single(n => n.FilePath == file);
            scan = vm.ScanAsync();
            Assert.IsTrue(vm.IsScanning);

            await vm.UnlockAndDeleteCommand.ExecuteAsync(file);
            var removedBeforeScanCompleted = vm.AllRecords.All(r => r.FilePath != file);
            var staleSnapshotPublished = false;
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(vm.AllRecords) && vm.AllRecords.Any(r => r.FilePath == file))
                {
                    staleSnapshotPublished = true;
                }
            };
            oldSnapshot.SetResult([deletedRecord, survivor]);
            await scan;

            Assert.IsFalse(File.Exists(file));
            Assert.IsTrue(removedBeforeScanCompleted, "Deletion should update the visible records immediately.");
            Assert.AreEqual(2, scans, "A refresh requested during an old scan must run after it.");
            Assert.IsFalse(staleSnapshotPublished, "An outdated scan must not reinsert the deleted row.");
            CollectionAssert.AreEqual(new[] { survivor }, vm.AllRecords);
            Assert.IsNull(vm.SelectedRecord);
            Assert.IsNull(vm.SelectedNode);
        }
        finally
        {
            oldSnapshot.TrySetResult([]);
            if (scan != null) await scan;
            vm.OnWindowClosing();
            File.Delete(file);
        }
    }

    [TestMethod]
    public async Task ScanLocksAsync_DeletedFileWithLiveCachedHandle_OmitsDeletedPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"kitopia_deleted_handle_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "delete.txt");
        var ready = Path.Combine(directory, "ready");
        File.WriteAllText(file, "delete fixture");
        using var child = Process.Start(new ProcessStartInfo("powershell.exe")
        {
            Arguments = $"-NoProfile -NonInteractive -Command \"$f = [System.IO.File]::Open('{file.Replace("'", "''")}', 'Open', 'ReadWrite', ([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete)); [System.IO.File]::WriteAllText('{ready.Replace("'", "''")}', 'ready'); Start-Sleep -Seconds 30\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
        Assert.IsNotNull(child);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!File.Exists(ready))
            {
                Assert.IsFalse(child.HasExited);
                await Task.Delay(50, timeout.Token);
            }

            IFileLockService service = new FileLocksmithService();
            var before = await service.ScanLocksAsync(rootDir: directory);
            Assert.IsTrue(before.Any(r => r.FilePath == file && r.ProcessId == child.Id));

            File.Delete(file);
            Assert.IsFalse(File.Exists(file));
            Assert.IsFalse(child.HasExited, "Keep the original handle alive to exercise the cached record.");

            var after = await service.ScanLocksAsync(rootDir: directory);
            Assert.IsFalse(after.Any(r => r.FilePath == file));
            Assert.AreEqual(0, (await service.CheckFileLocksAsync([file])).Count);
        }
        finally
        {
            if (!child.HasExited) child.Kill();
            await child.WaitForExitAsync();
            File.Delete(ready);
            File.Delete(file);
            Directory.Delete(directory);
        }
    }

    [TestMethod]
    public async Task UnlockAndDeleteCommand_DeleteFails_KeepsRowAndReportsError()
    {
        var record = new FileLockInfo { FilePath = Path.Combine(Path.GetTempPath(), "kitopia_delete_denied.txt"), State = "已锁定" };
        var service = new FileLockServiceStub { Records = [record], DeleteError = "Access denied" };
        var vm = new FileLocksmithWindowViewModel(service) { AutoRefresh = false };
        try
        {
            vm.LoadProcesses([record]);

            await vm.UnlockAndDeleteCommand.ExecuteAsync(record.FilePath);

            CollectionAssert.AreEqual(new[] { record }, vm.AllRecords);
            Assert.AreEqual(Lang.Format("lang.kitopia.messages.delete_failed_value", "Access denied"), vm.StatusMessage);
        }
        finally
        {
            vm.OnWindowClosing();
        }
    }

    [TestMethod]
    public async Task CheckFileLocksAsync_EmptyArray_ReturnsEmptyList()
    {
        IFileLockService service = new FileLocksmithService();
        var results = await service.CheckFileLocksAsync([]);
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public async Task ScanLocksAsync_LockedFile_DetectsLockingProcess()
    {
        IFileLockService service = new FileLocksmithService();
        var tempFile = Path.Combine(Path.GetTempPath(), $"kitopia_test_lock_{Guid.NewGuid():N}.tmp");

        Process? childProc = null;
        try
        {
            File.WriteAllText(tempFile, "test lock file");

            // Launch child process to hold exclusive lock (self PID is skipped by scanner)
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"$f = [System.IO.File]::Open('{tempFile.Replace("'", "''")}', [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None); Start-Sleep -Seconds 10\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            childProc = Process.Start(psi);
            Assert.IsNotNull(childProc);

            // Wait briefly for powershell to open the file
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(100);
                try
                {
                    using var s = new FileStream(tempFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException)
                {
                    // Locked!
                    break;
                }
            }

            var results = await service.ScanLocksAsync(targetPaths: [tempFile]);
            Assert.IsTrue(results.Count > 0, "Expected at least one lock record for exclusively opened file.");

            var lockedRecord = results.FirstOrDefault(r => r.ProcessId == childProc.Id);
            Assert.IsNotNull(lockedRecord, $"Expected child process (PID={childProc.Id}) to be detected as locking the file.");
            Assert.IsTrue(lockedRecord.IsLocked, "Expected file state to be locked.");
            Assert.IsTrue(string.Equals(tempFile, lockedRecord.FilePath, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (childProc != null && !childProc.HasExited)
            {
                try { childProc.Kill(); } catch { }
            }

            if (File.Exists(tempFile))
            {
                for (int i = 0; i < 10; i++)
                {
                    try
                    {
                        File.Delete(tempFile);
                        break;
                    }
                    catch
                    {
                        await Task.Delay(100);
                    }
                }
            }
        }
    }

    [TestMethod]
    public async Task UnlockFileAsync_EmptyList_ReturnsTrue()
    {
        IFileLockService service = new FileLocksmithService();
        var result = await service.UnlockFileAsync([]);
        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task UnlockFileAsync_RunningProcess_TerminatesWithoutElevation()
    {
        using var child = Process.Start(new ProcessStartInfo("powershell.exe")
        {
            Arguments = "-NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 30\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
        Assert.IsNotNull(child);

        try
        {
            IFileLockService service = new FileLocksmithService();
            Assert.IsTrue(await service.UnlockFileAsync([child.Id, child.Id]));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await child.WaitForExitAsync(timeout.Token);
            Assert.IsTrue(child.HasExited);
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
        }
    }

    [TestMethod]
    public async Task UnlockFileAsync_ExitedProcess_ReturnsTrueWithoutElevation()
    {
        IFileLockService service = new FileLocksmithService();
        Assert.IsTrue(await service.UnlockFileAsync([int.MaxValue]));
    }

    [TestMethod]
    public async Task UnlockFileAsync_CurrentAndSystemProcesses_SkipsWithoutElevation()
    {
        IFileLockService service = new FileLocksmithService();
        Assert.IsTrue(await service.UnlockFileAsync([Environment.ProcessId, 1, 4]));
    }

    [TestMethod]
    public async Task UnlockFileAsync_Cancelled_DoesNotTerminateProcess()
    {
        IFileLockService service = new FileLocksmithService();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.UnlockFileAsync([int.MaxValue], cancellation.Token));
    }

    [TestMethod]
    public async Task UnlockAndDeleteFileAsync_ExistingFile_DeletesSuccessfully()
    {
        IFileLockService service = new FileLocksmithService();
        var tempFile = Path.Combine(Path.GetTempPath(), $"kitopia_del_{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempFile, "delete test");
        Assert.IsTrue(File.Exists(tempFile));

        var error = await service.UnlockAndDeleteFileAsync(tempFile);
        Assert.IsNull(error);
        Assert.IsFalse(File.Exists(tempFile));
    }

    [TestMethod]
    public async Task ScanLocksAsync_LockedDirectory_DetectsDirectoryLockingProcess()
    {
        IFileLockService service = new FileLocksmithService();
        var tempDir = Path.Combine(Path.GetTempPath(), $"kitopia_dir_lock_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        Process? childProc = null;
        try
        {
            // Launch cmd.exe with WorkingDirectory set to tempDir so it holds a directory handle
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"Start-Sleep -Seconds 10\"",
                WorkingDirectory = tempDir,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            childProc = Process.Start(psi);
            Assert.IsNotNull(childProc);

            // Wait briefly for powershell to initialize in the working directory
            await Task.Delay(500);

            var results = await service.ScanLocksAsync(rootDir: tempDir);
            var dirLock = results.FirstOrDefault(r => r.ProcessId == childProc.Id && string.Equals(r.FilePath, tempDir, StringComparison.OrdinalIgnoreCase));

            Assert.IsNotNull(dirLock, $"Expected child process (PID={childProc.Id}) holding working directory {tempDir} to be detected.");
            Assert.IsTrue(dirLock.IsLocked, "Expected directory state to be locked.");
        }
        finally
        {
            if (childProc != null && !childProc.HasExited)
            {
                try { childProc.Kill(); } catch { }
            }

            if (Directory.Exists(tempDir))
            {
                for (int i = 0; i < 10; i++)
                {
                    try
                    {
                        Directory.Delete(tempDir, true);
                        break;
                    }
                    catch
                    {
                        await Task.Delay(100);
                    }
                }
            }
        }
    }

    [TestMethod]
    public void TreeExpansionState_PreservedAcrossFilterAndRebuild()
    {
        IFileLockService service = new FileLocksmithService();
        var vm = new FileLocksmithWindowViewModel(service);

        string root = Path.Combine(Path.GetTempPath(), "KitopiaTreeTestRoot");
        string subDir = Path.Combine(root, "UnoccupiedSubDir");
        string file1 = Path.Combine(subDir, "unlocked.txt");
        string file2 = Path.Combine(root, "locked.txt");

        var records = new List<FileLockInfo>
        {
            new() { FilePath = file1, ProcessId = 0, State = "空闲" },
            new() { FilePath = file2, ProcessId = 9999, ProcessName = "testproc.exe", State = "已锁定" }
        };

        vm.RootDir = root;
        vm.LoadProcesses(records);

        Assert.IsTrue(vm.TreeNodes.Count > 0, "Tree should have nodes");
        var rootNode = vm.TreeNodes.First();

        var subDirNode = rootNode.Children.FirstOrDefault(c => c.IsDirectory && c.FilePath.Equals(subDir, StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(subDirNode, "SubDir node should exist");
        Assert.IsFalse(subDirNode.HasChildLocks, "SubDir should have no locks");

        // User manually expands subDirNode
        subDirNode.IsExpanded = true;

        // Trigger filter change which rebuilds tree
        vm.FilterText = "txt";

        var rebuiltRoot = vm.TreeNodes.First();
        var rebuiltSubDir = rebuiltRoot.Children.FirstOrDefault(c => c.IsDirectory && c.FilePath.Equals(subDir, StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(rebuiltSubDir, "Rebuilt SubDir node should exist");
        Assert.IsTrue(rebuiltSubDir.IsExpanded, "User's expansion state on SubDir must be preserved across rebuild!");

        // User collapses all
        vm.CollapseAll();
        Assert.IsFalse(rebuiltSubDir.IsExpanded, "SubDir should be collapsed by CollapseAll");

        // Trigger another filter rebuild
        vm.FilterText = "";
        var rebuiltRoot2 = vm.TreeNodes.First();
        var rebuiltSubDir2 = rebuiltRoot2.Children.FirstOrDefault(c => c.IsDirectory && c.FilePath.Equals(subDir, StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(rebuiltSubDir2);
        Assert.IsFalse(rebuiltSubDir2.IsExpanded, "Collapsed state must be preserved across rebuild!");
    }

    [TestMethod]
    public async Task ScanLocksAsync_TargetPaths_ReturnsUnoccupiedFiles()
    {
        IFileLockService service = new FileLocksmithService();
        var tempFile1 = Path.Combine(Path.GetTempPath(), $"kitopia_target1_{Guid.NewGuid():N}.txt");
        var tempFile2 = Path.Combine(Path.GetTempPath(), $"kitopia_target2_{Guid.NewGuid():N}.txt");
        var tempFile3 = Path.Combine(Path.GetTempPath(), $"kitopia_target3_{Guid.NewGuid():N}.txt");

        try
        {
            File.WriteAllText(tempFile1, "hello 1");
            File.WriteAllText(tempFile2, "hello 2");
            File.WriteAllText(tempFile3, "hello 3");

            var results = await service.ScanLocksAsync(targetPaths: [tempFile1, tempFile2, tempFile3]);

            Assert.AreEqual(3, results.Count, "All 3 target files must be present in scan results even when unoccupied");
            Assert.IsTrue(results.All(r => !r.IsLocked), "Unoccupied files should not be marked as locked");
            Assert.IsTrue(results.All(r => r.State == "空闲"), "Unoccupied files should have state '空闲'");
        }
        finally
        {
            try { File.Delete(tempFile1); } catch { }
            try { File.Delete(tempFile2); } catch { }
            try { File.Delete(tempFile3); } catch { }
        }
    }

    [TestMethod]
    public void BuildTree_MultipleTargetFiles_DisplayedDirectlyAtRoot()
    {
        IFileLockService service = new FileLocksmithService();
        var vm = new FileLocksmithWindowViewModel(service);

        string f1 = @"C:\FakeFolderA\sub\file1.txt";
        string f2 = @"C:\FakeFolderB\file2.txt";
        string f3 = @"C:\FakeFolderC\deep\sub\file3.txt";

        var records = new List<FileLockInfo>
        {
            new() { FilePath = f1, ProcessId = 100, ProcessName = "app1.exe", State = "已锁定" },
            new() { FilePath = f2, ProcessId = 0, State = "空闲" },
            new() { FilePath = f3, ProcessId = 200, ProcessName = "app3.exe", State = "已锁定" }
        };

        vm.InitializeScope(rootDir: null, targetPaths: [f1, f2, f3]);
        vm.LoadProcesses(records);

        // Verify that all 3 target files appear directly as ROOT items in TreeNodes
        Assert.AreEqual(3, vm.TreeNodes.Count, "TreeNodes must directly contain the 3 target files at the root level");
        Assert.IsTrue(vm.TreeNodes.All(n => n.IsFile), "All root nodes should be File nodes, not parent directory nodes");
        CollectionAssert.AreEquivalent(
            new[] { Path.GetFullPath(f1), Path.GetFullPath(f2), Path.GetFullPath(f3) },
            vm.TreeNodes.Select(n => n.FilePath).ToList());
    }
}

