using Kitopia.Desktop.Abstractions;

namespace Kitopia.Desktop.Platform.Linux;

public sealed class LinuxDesktopPlatformInfo : IDesktopPlatformInfo
{
    public string OperatingSystemName => "Linux";
}
