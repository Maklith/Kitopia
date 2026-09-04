using System.Diagnostics;
using Kitopia.Desktop.Abstractions.FileSystem;
using Kitopia.Desktop.Features.ViewModel.Windows;
using Kitopia.Desktop.Platform.Windows.Services;

namespace KitopiaTest.Services;

[TestClass]
public sealed class FileLocksmithServiceTests
{
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

