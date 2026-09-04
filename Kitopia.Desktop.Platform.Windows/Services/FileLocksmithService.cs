using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Kitopia.Desktop.Abstractions.FileSystem;
using Microsoft.Win32;

namespace Kitopia.Desktop.Platform.Windows.Services;

public sealed class FileLocksmithService : IFileLockService
{
    public const string StateLocked = "已锁定";
    public const string StateDriver = "驱动锁定";
    public const string StateFree = "空闲";
    public const string StateDir = "目录";
    public const string StateGone = "已消失";
    public const string StateRestricted = "受限";

    private static bool _privilegeDone;
    private static readonly ConcurrentDictionary<(int Pid, nint Handle), CachedHandle> HandleCache = new();
    private static Dictionary<int, string> _procNames = new();
    private static int _scanCount;

    private sealed class CachedHandle
    {
        public string DosPath = "";
        public bool Seen;
    }

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
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedPaths.Length == 0)
        {
            return Task.FromResult(new List<FileLockInfo>());
        }

        if (normalizedPaths.Length == 1 && Directory.Exists(normalizedPaths[0]))
        {
            return ScanLocksAsync(rootDir: normalizedPaths[0], includeSubDirs: true, targetPaths: null, cancellationToken);
        }

        return ScanLocksAsync(rootDir: null, includeSubDirs: true, targetPaths: normalizedPaths, cancellationToken);
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
            var allSuccess = true;
            int selfPid = Environment.ProcessId;

            foreach (var pid in processIds.Distinct())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (pid == selfPid || pid <= 4)
                {
                    continue;
                }

                try
                {
                    using var process = Process.GetProcessById(pid);
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                    allSuccess = false;
                }
            }

            return allSuccess;
        }, cancellationToken);
    }

    public Task<List<FileLockInfo>> ScanLocksAsync(
        string? rootDir = null,
        bool includeSubDirs = true,
        IReadOnlyCollection<string>? targetPaths = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureDebugPrivilege();

            _scanCount++;
            if (_procNames.Count == 0 || _scanCount % 5 == 0)
            {
                _procNames = GetProcessNames();
            }

            var driveMap = GetDriveMap();
            var seen = new ConcurrentDictionary<(int, string), byte>();
            var rawResults = new ConcurrentBag<(int Pid, string ProcessName, string FilePath, bool IsDriver)>();
            int selfPid = Environment.ProcessId;

            HashSet<string>? targetSet = null;
            if (targetPaths != null && targetPaths.Count > 0)
            {
                targetSet = new HashSet<string>(
                    targetPaths.Select(Path.GetFullPath),
                    StringComparer.OrdinalIgnoreCase);
            }

            string? normalizedRoot = null;
            if (!string.IsNullOrWhiteSpace(rootDir))
            {
                normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDir));
            }

            using var handleTable = GetHandleTable();
            if (handleTable.Count > 0 && handleTable.First != IntPtr.Zero)
            {
                Parallel.For(0L, handleTable.Count,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount * 4, 8, 64),
                        CancellationToken = cancellationToken
                    },
                    i =>
                    {
                        var e = Marshal.PtrToStructure<NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>(
                            (IntPtr)((nint)handleTable.First + (nint)(i * handleTable.Stride)));
                        int pid = (int)e.ProcessId.ToUInt32();
                        if (pid == 0 || pid == selfPid) return;

                        var key = (pid, (nint)e.Handle);
                        if (HandleCache.TryGetValue(key, out CachedHandle? cached))
                        {
                            cached.Seen = true;
                            if (IsPathMatching(cached.DosPath, normalizedRoot, includeSubDirs, targetSet))
                            {
                                if (seen.TryAdd((pid, cached.DosPath), 0))
                                {
                                    string pname0 = _procNames.TryGetValue(pid, out string? n0) ? n0 : $"PID {pid}";
                                    rawResults.Add((pid, pname0, cached.DosPath, false));
                                }
                            }
                            return;
                        }

                        IntPtr dup = TryDuplicate(pid, e.Handle);
                        if (dup == IntPtr.Zero) return;

                        try
                        {
                            if (NativeMethods.GetFileType(dup) != 1) return;

                            string typeName = QueryString(dup, NativeMethods.ObjectTypeInformation) ?? "";
                            if (!string.Equals(typeName, "File", StringComparison.Ordinal)) return;

                            string ntPath = QueryString(dup, NativeMethods.ObjectNameInformation) ?? "";
                            if (string.IsNullOrEmpty(ntPath)) return;

                            if (!TryNtToDosPath(ntPath, driveMap, out string dosPath)) return;

                            HandleCache[key] = new CachedHandle { DosPath = dosPath, Seen = true };

                            if (IsPathMatching(dosPath, normalizedRoot, includeSubDirs, targetSet))
                            {
                                if (seen.TryAdd((pid, dosPath), 0))
                                {
                                    string pname = _procNames.TryGetValue(pid, out string? n) ? n : $"PID {pid}";
                                    rawResults.Add((pid, pname, dosPath, false));
                                }
                            }
                        }
                        finally
                        {
                            NativeMethods.CloseHandle(dup);
                        }
                    });
            }

            foreach (var kv in HandleCache)
            {
                if (kv.Value.Seen)
                {
                    kv.Value.Seen = false;
                }
                else
                {
                    HandleCache.TryRemove(kv.Key, out _);
                }
            }

            AddLoadedDrivers(rawResults, seen, normalizedRoot, includeSubDirs, targetSet);

            if (targetPaths != null && targetPaths.Count > 0)
            {
                QueryRestartManagerForFiles(targetPaths, rawResults, seen, cancellationToken);
            }

            var rawList = rawResults.ToList();
            var distinctPaths = rawList.Select(r => r.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var states = ProbeFileStates(distinctPaths, cancellationToken);

            if (!string.IsNullOrWhiteSpace(normalizedRoot) && Directory.Exists(normalizedRoot))
            {
                AddUnoccupiedDirectoryFiles(rawList, seen, states, normalizedRoot, includeSubDirs, cancellationToken);
            }

            if (targetPaths != null && targetPaths.Count > 0)
            {
                AddTargetPaths(rawList, seen, states, targetPaths, includeSubDirs, cancellationToken);
            }

            var finalResults = new List<FileLockInfo>(rawList.Count);
            foreach (var r in rawList)
            {
                string state;
                if (r.IsDriver)
                {
                    state = StateDriver;
                }
                else if (Directory.Exists(r.FilePath))
                {
                    state = r.Pid > 0 ? StateLocked : StateFree;
                }
                else if (r.Pid > 0)
                {
                    state = StateLocked;
                }
                else
                {
                    state = states.TryGetValue(r.FilePath, out string? s) ? s : StateRestricted;
                }

                string execPath = "";
                if (r.Pid > 4)
                {
                    try
                    {
                        using var p = Process.GetProcessById(r.Pid);
                        execPath = p.MainModule?.FileName ?? "";
                    }
                    catch
                    {
                    }
                }
                else if (r.IsDriver)
                {
                    execPath = r.FilePath;
                }

                finalResults.Add(new FileLockInfo
                {
                    ProcessId = r.Pid,
                    ProcessName = r.ProcessName,
                    ExecutablePath = execPath,
                    FilePath = r.FilePath,
                    State = state,
                    IsDriverModule = r.IsDriver,
                    LockedFiles = [r.FilePath]
                });
            }

            return finalResults
                .OrderByDescending(r => r.IsLocked)
                .ThenBy(r => r.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.ProcessId)
                .ToList();
        }, cancellationToken);
    }

    public Task<string?> StopDriverForFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            string? svc = FindServiceByImagePath(filePath);
            if (string.IsNullOrEmpty(svc))
            {
                return $"未找到加载 {filePath} 的系统驱动服务。";
            }

            (int code, string output) = RunSc($"stop \"{svc}\"");
            return code == 0
                ? null
                : $"停止驱动服务 [{svc}] 失败 (错误码 {code}): {output.Split('\n').FirstOrDefault()?.Trim()}";
        }, cancellationToken);
    }

    public async Task<string?> UnlockAndDeleteFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return "文件路径不能为空。";
        }

        var records = await ScanLocksAsync(rootDir: null, includeSubDirs: false, targetPaths: [filePath], cancellationToken);
        var fileRecords = records.Where(r => string.Equals(r.FilePath, filePath, StringComparison.OrdinalIgnoreCase)).ToList();

        if (fileRecords.Any(r => r.IsDriverModule))
        {
            var driverErr = await StopDriverForFileAsync(filePath, cancellationToken);
            if (driverErr != null)
            {
                return driverErr;
            }
        }

        var pids = fileRecords
            .Where(r => !r.IsDriverModule && r.ProcessId > 4)
            .Select(r => r.ProcessId)
            .Distinct()
            .ToList();

        if (pids.Count > 0)
        {
            await UnlockFileAsync(pids, cancellationToken);
        }

        return await Task.Run(() =>
        {
            for (int i = 0; i < 4; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!File.Exists(filePath)) return null;
                    File.Delete(filePath);
                    if (!File.Exists(filePath)) return null;
                }
                catch (Exception ex)
                {
                    if (i == 3)
                    {
                        return $"{ex.GetType().Name}: {ex.Message}";
                    }
                    Thread.Sleep(500);
                }
            }

            return File.Exists(filePath) ? "文件仍存在，删除失败。" : null;
        }, cancellationToken);
    }

    private static bool IsPathMatching(
        string filePath,
        string? rootDir,
        bool includeSubDirs,
        HashSet<string>? targetSet)
    {
        if (targetSet != null)
        {
            if (targetSet.Contains(filePath)) return true;
            foreach (var target in targetSet)
            {
                if (filePath.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        if (!string.IsNullOrEmpty(rootDir))
        {
            if (string.Equals(filePath, rootDir, StringComparison.OrdinalIgnoreCase)) return true;

            if (filePath.StartsWith(rootDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                if (!includeSubDirs)
                {
                    var relative = filePath[(rootDir.Length + 1)..];
                    if (relative.Contains(Path.DirectorySeparatorChar))
                    {
                        return false;
                    }
                }
                return true;
            }

            return false;
        }

        return true;
    }

    private sealed class HandleTableSnapshot : IDisposable
    {
        private IntPtr _buffer;
        public IntPtr First { get; }
        public long Count { get; }
        public int Stride { get; }

        public HandleTableSnapshot(IntPtr buffer, IntPtr first, long count, int stride)
        {
            _buffer = buffer;
            First = first;
            Count = count;
            Stride = stride;
        }

        public void Dispose()
        {
            if (_buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_buffer);
                _buffer = IntPtr.Zero;
            }
        }
    }

    private static HandleTableSnapshot GetHandleTable()
    {
        const int initial = 0x400000;
        const int max = 0x20000000;
        int size = initial;
        IntPtr buffer = Marshal.AllocHGlobal(size);

        while (true)
        {
            int status = NativeMethods.NtQuerySystemInformation(
                NativeMethods.SystemExtendedHandleInformation, buffer, size, out int ret);
            if (status == 0) break;
            if (status != NativeMethods.StatusInfoLengthMismatch || size >= max)
            {
                Marshal.FreeHGlobal(buffer);
                return new HandleTableSnapshot(IntPtr.Zero, IntPtr.Zero, 0, 0);
            }

            size = Math.Max(size * 2, ret + 0x10000);
            Marshal.FreeHGlobal(buffer);
            buffer = Marshal.AllocHGlobal(size);
        }

        long count = Marshal.ReadIntPtr(buffer).ToInt64();
        int stride = Marshal.SizeOf<NativeMethods.SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX>();
        return new HandleTableSnapshot(buffer, buffer + IntPtr.Size * 2, count, stride);
    }

    private static IntPtr TryDuplicate(int pid, IntPtr handle)
    {
        IntPtr proc = NativeMethods.OpenProcess(NativeMethods.PROCESS_DUP_HANDLE, false, pid);
        if (proc == IntPtr.Zero) return IntPtr.Zero;
        try
        {
            return NativeMethods.DuplicateHandle(proc, handle, NativeMethods.GetCurrentProcess(),
                out IntPtr dup, 0, false, NativeMethods.DUPLICATE_SAME_ACCESS)
                ? dup
                : IntPtr.Zero;
        }
        finally
        {
            NativeMethods.CloseHandle(proc);
        }
    }

    private static string? QueryString(IntPtr handle, int infoClass)
    {
        int size = 0x1000;
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            int status = NativeMethods.NtQueryObject(handle, infoClass, buf, size, out int needed);
            if (status == NativeMethods.StatusInfoLengthMismatch && needed > 0)
            {
                Marshal.FreeHGlobal(buf);
                size = needed;
                buf = Marshal.AllocHGlobal(size);
                status = NativeMethods.NtQueryObject(handle, infoClass, buf, size, out _);
            }

            if (status != 0) return null;
            var us = Marshal.PtrToStructure<NativeMethods.UNICODE_STRING>(buf);
            if (us.Buffer == IntPtr.Zero || us.Length == 0) return null;
            return Marshal.PtrToStringUni(us.Buffer, us.Length / 2);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static void EnsureDebugPrivilege()
    {
        if (_privilegeDone) return;
        _privilegeDone = true;
        try
        {
            if (!NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(),
                    NativeMethods.TOKEN_ADJUST_PRIVILEGES | NativeMethods.TOKEN_QUERY, out IntPtr token))
            {
                return;
            }

            try
            {
                if (!NativeMethods.LookupPrivilegeValue(null, "SeDebugPrivilege", out NativeMethods.LUID luid)) return;
                var tp = new NativeMethods.TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = NativeMethods.SE_PRIVILEGE_ENABLED
                };
                NativeMethods.AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                NativeMethods.CloseHandle(token);
            }
        }
        catch
        {
        }
    }

    private static void AddLoadedDrivers(
        ConcurrentBag<(int Pid, string ProcessName, string FilePath, bool IsDriver)> results,
        ConcurrentDictionary<(int, string), byte> seen,
        string? rootDir,
        bool includeSubDirs,
        HashSet<string>? targetSet)
    {
        try
        {
            if (!NativeMethods.EnumDeviceDrivers(null, 0, out int needed)) return;
            int count = needed / IntPtr.Size;
            if (count <= 0) return;
            var bases = new IntPtr[count];
            if (!NativeMethods.EnumDeviceDrivers(bases, count * IntPtr.Size, out _)) return;

            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var sb = new StringBuilder(1024);
            foreach (IntPtr b in bases)
            {
                if (b == IntPtr.Zero) continue;
                sb.Clear();
                if (NativeMethods.GetDeviceDriverFileName(b, sb, sb.Capacity) <= 0) continue;
                string? dos = NormalizeImagePath(sb.ToString(), winDir);
                if (dos == null) continue;

                if (!IsPathMatching(dos, rootDir, includeSubDirs, targetSet)) continue;
                if (!seen.TryAdd((4, dos), 0)) continue;

                results.Add((4, "System(驱动模块)", dos, true));
            }
        }
        catch
        {
        }
    }

    public static string? NormalizeImagePath(string raw, string winDir)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        string p = raw.Trim().TrimEnd('\0');
        if (p.StartsWith("\\??\\", StringComparison.OrdinalIgnoreCase))
            p = p[4..];
        else if (p.StartsWith("\\SystemRoot\\", StringComparison.OrdinalIgnoreCase))
            p = winDir + "\\" + p[12..];
        else if (p.StartsWith("\\Windows\\", StringComparison.OrdinalIgnoreCase))
            p = Path.GetPathRoot(winDir) + p;
        if (!Path.IsPathRooted(p)) p = Path.Combine(winDir, p);
        try { p = Path.GetFullPath(p); }
        catch { return null; }
        return p.Length >= 3 && char.IsLetter(p[0]) && p[1] == ':' ? p : null;
    }

    public static string? FindServiceByImagePath(string filePath)
    {
        try
        {
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (root == null) return null;
            foreach (string svc in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(svc);
                if (k?.GetValue("ImagePath") is not string img) continue;
                string? norm = NormalizeImagePath(img, winDir);
                if (norm != null && string.Equals(norm, filePath, StringComparison.OrdinalIgnoreCase))
                    return svc;
            }
        }
        catch
        {
        }
        return null;
    }

    private static Dictionary<string, string> ProbeFileStates(
        IEnumerable<string> paths,
        CancellationToken cancellationToken)
    {
        var states = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Parallel.ForEach(paths,
            new ParallelOptions { MaxDegreeOfParallelism = 32, CancellationToken = cancellationToken },
            path => states[path] = ProbeOne(path));
        return new Dictionary<string, string>(states, StringComparer.OrdinalIgnoreCase);
    }

    private static string ProbeOne(string path)
    {
        try
        {
            if (File.GetAttributes(path).HasFlag(FileAttributes.Directory)) return StateDir;
        }
        catch (FileNotFoundException) { return StateGone; }
        catch (DirectoryNotFoundException) { return StateGone; }
        catch { }

        try
        {
            using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.None, 1, FileOptions.None);
            return StateFree;
        }
        catch (Exception ex)
        {
            if (ex is FileNotFoundException or DirectoryNotFoundException) return StateGone;
            if (ex.HResult == unchecked((int)0x80070020)) return StateLocked;
            if (ex.HResult == unchecked((int)0x80070021)) return StateLocked;
            if (ex is UnauthorizedAccessException) return StateRestricted;
            if (ex is IOException) return StateLocked;
            return StateRestricted;
        }
    }

    private static void QueryRestartManagerForFiles(
        IEnumerable<string> filePaths,
        ConcurrentBag<(int Pid, string ProcessName, string FilePath, bool IsDriver)> results,
        ConcurrentDictionary<(int, string), byte> seen,
        CancellationToken cancellationToken)
    {
        int selfPid = Environment.ProcessId;

        foreach (var rawPath in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.GetFullPath(rawPath);
            if (!File.Exists(path)) continue;

            string sessionKey = Guid.NewGuid().ToString();
            int res = NativeMethods.RmStartSession(out uint sessionHandle, 0, sessionKey);
            if (res != NativeMethods.ErrorSuccess) continue;

            try
            {
                string[] rgsFiles = [path];
                res = NativeMethods.RmRegisterResources(sessionHandle, 1, rgsFiles, 0, null, 0, null);
                if (res != NativeMethods.ErrorSuccess) continue;

                uint pnProcInfo = 0;
                NativeMethods.RmProcessInfo[] rgAffectedApps = [];
                uint lpdwRebootReasons = 0;

                res = NativeMethods.RmGetList(sessionHandle, out uint pnProcInfoNeeded, ref pnProcInfo, rgAffectedApps, ref lpdwRebootReasons);
                if (res == NativeMethods.ErrorMoreData && pnProcInfoNeeded > 0)
                {
                    pnProcInfo = pnProcInfoNeeded;
                    rgAffectedApps = new NativeMethods.RmProcessInfo[pnProcInfo];
                    res = NativeMethods.RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, rgAffectedApps, ref lpdwRebootReasons);
                }

                if (res == NativeMethods.ErrorSuccess && rgAffectedApps.Length > 0)
                {
                    foreach (var app in rgAffectedApps)
                    {
                        int pid = app.Process.dwProcessId;
                        if (pid <= 0 || pid == selfPid) continue;

                        string pname = !string.IsNullOrWhiteSpace(app.strAppName)
                            ? app.strAppName
                            : (_procNames.TryGetValue(pid, out string? n) ? n : $"PID {pid}");

                        if (seen.TryAdd((pid, path), 0))
                        {
                            results.Add((pid, pname, path, false));
                        }
                    }
                }
            }
            catch
            {
            }
            finally
            {
                NativeMethods.RmEndSession(sessionHandle);
            }
        }
    }

    private static void AddTargetPaths(
        List<(int Pid, string ProcessName, string FilePath, bool IsDriver)> list,
        ConcurrentDictionary<(int, string), byte> seen,
        Dictionary<string, string> states,
        IReadOnlyCollection<string> targetPaths,
        bool includeSubDirs,
        CancellationToken cancellationToken)
    {
        var occupied = new HashSet<string>(
            list.Select(r => r.FilePath), StringComparer.OrdinalIgnoreCase);

        var pendingFiles = new List<string>();

        foreach (var rawPath in targetPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.GetFullPath(rawPath);

            if (Directory.Exists(path))
            {
                string normDir = Path.TrimEndingDirectorySeparator(path);
                if (!occupied.Contains(normDir) && seen.TryAdd((0, normDir), 0))
                {
                    list.Add((0, "无进程占用", normDir, false));
                    states[normDir] = StateFree;
                }

                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = includeSubDirs,
                    IgnoreInaccessible = true,
                    AttributesToSkip = 0
                };

                try
                {
                    foreach (string file in Directory.EnumerateFiles(normDir, "*", options))
                    {
                        if (pendingFiles.Count >= 3000) break;
                        if (!occupied.Contains(file))
                        {
                            pendingFiles.Add(file);
                        }
                    }
                }
                catch
                {
                }
            }
            else if (File.Exists(path))
            {
                if (!occupied.Contains(path))
                {
                    pendingFiles.Add(path);
                }
            }
            else
            {
                if (seen.TryAdd((0, path), 0))
                {
                    list.Add((0, "文件不存在", path, false));
                    states[path] = StateGone;
                }
            }
        }

        if (pendingFiles.Count > 0)
        {
            foreach (var kv in ProbeFileStates(pendingFiles, cancellationToken))
            {
                states[kv.Key] = kv.Value;
                if (seen.TryAdd((0, kv.Key), 0))
                {
                    string pName = kv.Value == StateLocked
                        ? "占用进程未知 (权限受限或系统保护)"
                        : "无进程占用";
                    list.Add((0, pName, kv.Key, false));
                }
            }
        }
    }

    private static void AddUnoccupiedDirectoryFiles(
        List<(int Pid, string ProcessName, string FilePath, bool IsDriver)> list,
        ConcurrentDictionary<(int, string), byte> seen,
        Dictionary<string, string> states,
        string rootDir,
        bool includeSubDirs,
        CancellationToken cancellationToken)
    {
        try
        {
            var occupied = new HashSet<string>(
                list.Select(r => r.FilePath), StringComparer.OrdinalIgnoreCase);

            if (!occupied.Contains(rootDir) && seen.TryAdd((0, rootDir), 0))
            {
                list.Add((0, "无进程占用", rootDir, false));
                states[rootDir] = StateFree;
            }

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = includeSubDirs,
                IgnoreInaccessible = true,
                AttributesToSkip = 0
            };

            var pending = new List<string>();
            foreach (string path in Directory.EnumerateFiles(rootDir, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (pending.Count >= 3000) break;
                if (!occupied.Contains(path))
                {
                    pending.Add(path);
                }
            }

            foreach (var kv in ProbeFileStates(pending, cancellationToken))
            {
                states[kv.Key] = kv.Value;
                if (seen.TryAdd((0, kv.Key), 0))
                {
                    string pName = kv.Value == StateLocked
                        ? "占用进程未知 (权限受限或系统保护)"
                        : "无进程占用";
                    list.Add((0, pName, kv.Key, false));
                }
            }
        }
        catch
        {
        }
    }

    private static Dictionary<string, string> GetDriveMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (char c = 'A'; c <= 'Z'; c++)
        {
            var sb = new StringBuilder(1024);
            if (NativeMethods.QueryDosDevice(c + ":", sb, sb.Capacity) > 0)
            {
                map[sb.ToString().TrimEnd('\0')] = c + ":";
            }
        }
        return map;
    }

    private static bool TryNtToDosPath(string ntPath, Dictionary<string, string> map, out string dosPath)
    {
        dosPath = string.Empty;
        if (string.IsNullOrEmpty(ntPath)) return false;
        if (ntPath.StartsWith("\\Device\\NamedPipe", StringComparison.OrdinalIgnoreCase) ||
            ntPath.StartsWith("\\Device\\MailSlot", StringComparison.OrdinalIgnoreCase) ||
            ntPath.StartsWith("\\Device\\Mup", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var kv in map)
        {
            if (ntPath.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
            {
                dosPath = kv.Value + ntPath[kv.Key.Length..];
                return true;
            }
        }
        return false;
    }

    private static Dictionary<int, string> GetProcessNames()
    {
        var names = new Dictionary<int, string>();
        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                names[p.Id] = p.ProcessName + ".exe";
            }
            catch
            {
            }
            finally
            {
                p.Dispose();
            }
        }
        return names;
    }

    private static (int Code, string Output) RunSc(string args)
    {
        try
        {
            using Process p = Process.Start(new ProcessStartInfo("sc.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            })!;
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(15000);
            return (p.ExitCode, output.Trim());
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    internal static class NativeMethods
    {
        private const string NtDll = "ntdll.dll";
        private const string Kernel32 = "kernel32.dll";
        private const string AdvApi32 = "advapi32.dll";
        private const string Psapi = "psapi.dll";
        private const string RstrtMgr = "rstrtmgr.dll";

        internal const int SystemExtendedHandleInformation = 64;
        internal const int ObjectNameInformation = 1;
        internal const int ObjectTypeInformation = 2;
        internal const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
        internal const uint PROCESS_DUP_HANDLE = 0x0040;
        internal const uint DUPLICATE_SAME_ACCESS = 0x0002;
        internal const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        internal const uint TOKEN_QUERY = 0x0008;
        internal const int SE_PRIVILEGE_ENABLED = 0x0002;
        internal const int ErrorSuccess = 0;
        internal const int ErrorMoreData = 234;

        [StructLayout(LayoutKind.Sequential)]
        internal struct RmUniqueProcess
        {
            public int dwProcessId;
            public FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct RmProcessInfo
        {
            public RmUniqueProcess Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string strServiceShortName;
            public int ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [DllImport(RstrtMgr, CharSet = CharSet.Unicode)]
        internal static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport(RstrtMgr)]
        internal static extern int RmEndSession(uint dwSessionHandle);

        [DllImport(RstrtMgr, CharSet = CharSet.Unicode)]
        internal static extern int RmRegisterResources(uint dwSessionHandle, uint nFiles, string[] rgsFileNames,
            uint nApplications, [In] RmUniqueProcess[]? rgApplications, uint nServices,
            string[]? rgsServiceNames);

        [DllImport(RstrtMgr)]
        internal static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded,
            ref uint pnProcInfo, [In, Out] RmProcessInfo[] rgAffectedApps,
            ref uint lpdwRebootReasons);

        [StructLayout(LayoutKind.Sequential)]
        internal struct SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX
        {
            public IntPtr Object;
            public UIntPtr ProcessId;
            public IntPtr Handle;
            public uint GrantedAccess;
            public ushort CreatorBackTraceIndex;
            public ushort ObjectTypeIndex;
            public uint HandleAttributes;
            public uint Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct UNICODE_STRING
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct TOKEN_PRIVILEGES
        {
            public int PrivilegeCount;
            public LUID Luid;
            public int Attributes;
        }

        [DllImport(NtDll)]
        internal static extern int NtQuerySystemInformation(int infoClass, IntPtr info, int size, out int returnedSize);

        [DllImport(NtDll)]
        internal static extern int NtQueryObject(IntPtr handle, int infoClass, IntPtr info, int size, out int returnedSize);

        [DllImport(Kernel32, SetLastError = true)]
        internal static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport(Kernel32, SetLastError = true)]
        internal static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr sourceHandle, IntPtr targetProcess,
            out IntPtr targetHandle, uint desiredAccess, bool inherit, uint options);

        [DllImport(Kernel32)]
        internal static extern IntPtr GetCurrentProcess();

        [DllImport(Kernel32, SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr handle);

        [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern uint QueryDosDevice(string deviceName, StringBuilder targetPath, int max);

        [DllImport(Kernel32, SetLastError = true)]
        internal static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport(AdvApi32, SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern bool LookupPrivilegeValue(string? systemName, string privilegeName, out LUID luid);

        [DllImport(AdvApi32, SetLastError = true)]
        internal static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES newState,
            int bufferLength, IntPtr previousState, IntPtr returnLength);

        [DllImport(Psapi, SetLastError = true)]
        internal static extern bool EnumDeviceDrivers(IntPtr[]? imageBase, int cb, out int needed);

        [DllImport(Psapi, SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern int GetDeviceDriverFileName(IntPtr imageBase, StringBuilder buffer, int size);

        [DllImport(Kernel32, SetLastError = true)]
        internal static extern uint GetFileType(IntPtr handle);
    }
}
