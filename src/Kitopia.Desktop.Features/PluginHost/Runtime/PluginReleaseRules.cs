namespace Kitopia.Desktop.Features.Services.Plugin;

internal static class PluginReleaseRules
{
    public const string HostBundledPluginNameSign = "kitopiaex";
    public const string HostBundledCpuPluginNameSign = "kitopiaonnxruntimecpu";

    public static bool IsHostBundled(string? nameSign) =>
        string.Equals(nameSign, HostBundledPluginNameSign, StringComparison.OrdinalIgnoreCase) ||
        OperatingSystem.IsWindows() &&
        string.Equals(nameSign, HostBundledCpuPluginNameSign, StringComparison.OrdinalIgnoreCase);

    public static bool IsBundledPluginUpdateAllowed(string? nameSign) =>
        OperatingSystem.IsWindows() &&
        string.Equals(nameSign, HostBundledCpuPluginNameSign, StringComparison.OrdinalIgnoreCase);
}
