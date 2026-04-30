using System.Runtime.InteropServices;

namespace Core.Window.ScreenCapture;

public static class DisplayConfigHelper
{
    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(
        uint flags,
        out uint numPathArrayElements,
        out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [In, Out] DisplayconfigPathInfo[] pathArray,
        ref uint numModeInfoArrayElements,
        [In, Out] DisplayconfigModeInfo[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayconfigSdrWhiteLevel requestPacket);

    private const uint QdcOnlyActivePaths = 0x00000002;
    private const int ErrorSuccess = 0;
    private const uint DisplayconfigDeviceInfoGetSdrWhiteLevel = 11;

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayconfigPathInfo
    {
        public DisplayconfigPathSourceInfo sourceInfo;
        public DisplayconfigPathTargetInfo targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayconfigPathSourceInfo
    {
        public Luid adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayconfigPathTargetInfo
    {
        public Luid adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DisplayconfigRational refreshRate;
        public uint scanLineOrdering;
        public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayconfigRational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayconfigModeInfo
    {
        public uint infoType;
        public uint id;
        public Luid adapterId;
        public DisplayconfigTargetMode mode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayconfigTargetMode
    {
        public DisplayconfigVideoSignalInfo targetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayconfigVideoSignalInfo
    {
        public ulong pixelRate;
        public DisplayconfigRational hSyncFreq;
        public DisplayconfigRational vSyncFreq;
        public Displayconfig2Dregion activeSize;
        public Displayconfig2Dregion totalSize;
        public uint videoStandard;
        public uint scanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Displayconfig2Dregion
    {
        public uint cx;
        public uint cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct DisplayconfigDeviceInfoHeader
    {
        public uint type;
        public uint size;
        public Luid adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct DisplayconfigSdrWhiteLevel
    {
        public DisplayconfigDeviceInfoHeader header;
        public uint SDRWhiteLevel;
    }
    
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref Monitorinfoex lpmi);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayconfigSourceDeviceName requestPacket);

    private const uint DisplayconfigDeviceInfoGetSourceName = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct Monitorinfoex
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
    private struct DisplayconfigSourceDeviceName
    {
        public DisplayconfigDeviceInfoHeader header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }

    public static float GetSdrWhiteLevel(IntPtr hMonitor)
    {
        if (hMonitor == IntPtr.Zero) return 1.0f;

        var mi = new Monitorinfoex();
        mi.cbSize = Marshal.SizeOf(mi);
        if (!GetMonitorInfo(hMonitor, ref mi)) return 1.0f;

        var ret = GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var numPathArrayElements, out var numModeInfoArrayElements);
        if (ret != ErrorSuccess) return 1.0f;

        var pathArray = new DisplayconfigPathInfo[numPathArrayElements];
        var modeInfoArray = new DisplayconfigModeInfo[numModeInfoArrayElements];

        ret = QueryDisplayConfig(QdcOnlyActivePaths, ref numPathArrayElements, pathArray, ref numModeInfoArrayElements, modeInfoArray, IntPtr.Zero);
        if (ret != ErrorSuccess) return 1.0f;

        for (int i = 0; i < numPathArrayElements; i++)
        {
            var path = pathArray[i];
            
            var sourceName = new DisplayconfigSourceDeviceName();
            sourceName.header.type = DisplayconfigDeviceInfoGetSourceName;
            sourceName.header.size = (uint)Marshal.SizeOf(typeof(DisplayconfigSourceDeviceName));
            sourceName.header.adapterId = path.sourceInfo.adapterId;
            sourceName.header.id = path.sourceInfo.id;

            if (DisplayConfigGetDeviceInfo(ref sourceName) == ErrorSuccess)
            {
                if (sourceName.viewGdiDeviceName == mi.szDevice)
                {
                    // Found the path matching our monitor
                    var request = new DisplayconfigSdrWhiteLevel
                    {
                        header = new DisplayconfigDeviceInfoHeader
                        {
                            type = DisplayconfigDeviceInfoGetSdrWhiteLevel,
                            size = (uint)Marshal.SizeOf(typeof(DisplayconfigSdrWhiteLevel)),
                            adapterId = path.targetInfo.adapterId,
                            id = path.targetInfo.id
                        }
                    };
                    
                    if (DisplayConfigGetDeviceInfo(ref request) == ErrorSuccess)
                    {
                        if (request.SDRWhiteLevel > 0)
                        {
                            // 1000 = 80 nits
                            return request.SDRWhiteLevel / 1000.0f;
                        }
                    }
                }
            }
        }

        return 1.0f;
    }
}