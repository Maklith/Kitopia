using System;
using System.Runtime.InteropServices;

namespace Core.Window;

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
        [In, Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements,
        [In, Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SDR_WHITE_LEVEL requestPacket);

    private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    private const int ERROR_SUCCESS = 0;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL = 11;

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
        public DISPLAYCONFIG_TARGET_MODE mode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_TARGET_MODE
    {
        public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
    {
        public ulong pixelRate;
        public DISPLAYCONFIG_RATIONAL hSyncFreq;
        public DISPLAYCONFIG_RATIONAL vSyncFreq;
        public DISPLAYCONFIG_2DREGION activeSize;
        public DISPLAYCONFIG_2DREGION totalSize;
        public uint videoStandard;
        public uint scanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_2DREGION
    {
        public uint cx;
        public uint cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct DISPLAYCONFIG_SDR_WHITE_LEVEL
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint SDRWhiteLevel;
    }
    
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }

    public static float GetSdrWhiteLevel(IntPtr hMonitor)
    {
        if (hMonitor == IntPtr.Zero) return 1.0f;

        var mi = new MONITORINFOEX();
        mi.cbSize = Marshal.SizeOf(mi);
        if (!GetMonitorInfo(hMonitor, ref mi)) return 1.0f;

        uint numPathArrayElements = 0;
        uint numModeInfoArrayElements = 0;
        
        var ret = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out numPathArrayElements, out numModeInfoArrayElements);
        if (ret != ERROR_SUCCESS) return 1.0f;

        var pathArray = new DISPLAYCONFIG_PATH_INFO[numPathArrayElements];
        var modeInfoArray = new DISPLAYCONFIG_MODE_INFO[numModeInfoArrayElements];

        ret = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref numPathArrayElements, pathArray, ref numModeInfoArrayElements, modeInfoArray, IntPtr.Zero);
        if (ret != ERROR_SUCCESS) return 1.0f;

        for (int i = 0; i < numPathArrayElements; i++)
        {
            var path = pathArray[i];
            
            var sourceName = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
            sourceName.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
            sourceName.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME));
            sourceName.header.adapterId = path.sourceInfo.adapterId;
            sourceName.header.id = path.sourceInfo.id;

            if (DisplayConfigGetDeviceInfo(ref sourceName) == ERROR_SUCCESS)
            {
                if (sourceName.viewGdiDeviceName == mi.szDevice)
                {
                    // Found the path matching our monitor
                    var request = new DISPLAYCONFIG_SDR_WHITE_LEVEL
                    {
                        header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                        {
                            type = DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL,
                            size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SDR_WHITE_LEVEL)),
                            adapterId = path.targetInfo.adapterId,
                            id = path.targetInfo.id
                        }
                    };
                    
                    if (DisplayConfigGetDeviceInfo(ref request) == ERROR_SUCCESS)
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