namespace Kitopia.Desktop.Features.Services.Plugin;

internal static class PluginReleaseRules
{
    public const string HostBundledPluginNameSign = "kitopiaex";

    public static bool IsHostBundled(string? nameSign) =>
        string.Equals(nameSign, HostBundledPluginNameSign, StringComparison.OrdinalIgnoreCase);
}
