using System.Runtime.InteropServices;

namespace Kitopia.DeviceCommunication.Discovery;

public static class DeviceOperatingSystemName
{
    public static string ResolveCurrent()
    {
        if (OperatingSystem.IsAndroid())
        {
            return "Android";
        }

        if (OperatingSystem.IsIOS())
        {
            return "iOS";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "Windows";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "Linux";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "macOS";
        }

        return RuntimeInformation.OSDescription.Trim();
    }
}
