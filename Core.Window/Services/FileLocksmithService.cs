using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Core.Services.Interfaces;

namespace Core.Window.Services;

public class FileLocksmithService : IFileLocksmith
{
    public Task<List<LockingProcessInfo>> CheckFileLocksAsync(string[] filePaths)
    {
        return Task.Run(() =>
        {
            var results = new List<LockingProcessInfo>();
            if (filePaths.Length == 0) return results;

            string sessionKey = Guid.NewGuid().ToString();
            
            var res = NativeMethods.RmStartSession(out var sessionHandle, 0, sessionKey);
            if (res != NativeMethods.ErrorSuccess) return results;

            try
            {
                res = NativeMethods.RmRegisterResources(sessionHandle, (uint)filePaths.Length, filePaths, 0, null!, 0, null!);
                if (res != NativeMethods.ErrorSuccess) return results;

                uint pnProcInfo = 0;
                NativeMethods.RmProcessInfo[] rgAffectedApps = [];
                uint lpdwRebootReasons = 0;

                res = NativeMethods.RmGetList(sessionHandle, out var pnProcInfoNeeded, ref pnProcInfo, rgAffectedApps, ref lpdwRebootReasons);

                if (res == NativeMethods.ErrorMoreData)
                {
                    pnProcInfo = pnProcInfoNeeded;
                    rgAffectedApps = new NativeMethods.RmProcessInfo[pnProcInfo];
                    res = NativeMethods.RmGetList(sessionHandle, out pnProcInfoNeeded, ref pnProcInfo, rgAffectedApps, ref lpdwRebootReasons);
                }

                if (res == NativeMethods.ErrorSuccess)
                {
                    foreach (var app in rgAffectedApps)
                    {
                        var processInfo = new LockingProcessInfo
                        {
                            ProcessId = app.Process.dwProcessId,
                            ProcessName = app.strAppName,
                            StartTime = app.Process.ProcessStartTime.ToDateTime(),
                        };
                        
                        try
                        {
                            using var process = Process.GetProcessById(processInfo.ProcessId);
                            processInfo.ExecutablePath = process.MainModule?.FileName??"";
                        }
                        catch 
                        { 
                            // Process might have exited or access denied
                        }
                        
                        processInfo.LockedFiles.AddRange(filePaths); 
                        
                        results.Add(processInfo);
                    }
                }
            }
            finally
            {
                NativeMethods.RmEndSession(sessionHandle);
            }

            return results;
        });
    }

    public Task<bool> UnlockFileAsync(List<int> processIds)
    {
        return Task.Run(() =>
        {
            bool allSuccess = true;
            foreach (var pid in processIds)
            {
                try
                {
                    using var process = Process.GetProcessById(pid);
                    process.Kill();
                }
                catch (Exception)
                {
                    allSuccess = false;
                }
            }
            return allSuccess;
        });
    }

    internal static class NativeMethods
    {
        public const int ErrorSuccess = 0;
        public const int ErrorMoreData = 234;

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        public static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll")]
        public static extern int RmEndSession(uint dwSessionHandle);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        public static extern int RmRegisterResources(uint dwSessionHandle, uint nFiles, string[] rgsFileNames,
            uint nApplications, [In] RmUniqueProcess[] rgApplications, uint nServices,
            string[] rgsServiceNames);

        [DllImport("rstrtmgr.dll")]
        public static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded,
            ref uint pnProcInfo, [In, Out] RmProcessInfo[] rgAffectedApps,
            ref uint lpdwRebootReasons);

        [StructLayout(LayoutKind.Sequential)]
        public struct RmUniqueProcess
        {
            public int dwProcessId;
            public FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct RmProcessInfo
        {
            public RmUniqueProcess Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string strServiceShortName;
            public RmAppType ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        public enum RmAppType
        {
            RmUnknownApp = 0,
            RmMainWindow = 1,
            RmOtherWindow = 2,
            RmService = 3,
            RmExplorer = 4,
            RmConsole = 5,
            RmCritical = 1000
        }
    }
}

internal static class Extensions
{
    public static DateTime ToDateTime(this FILETIME fileTime)
    {
        long high = (long)fileTime.dwHighDateTime << 32;
        long low = fileTime.dwLowDateTime & 0xFFFFFFFFL;
        return DateTime.FromFileTime(high | low);
    }
}
