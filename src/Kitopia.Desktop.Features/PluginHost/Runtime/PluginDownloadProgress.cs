using Kitopia.Feature.Localization;
using PluginCore;

namespace Kitopia.Desktop.Features.Services.Plugin;

public sealed record PluginDownloadProgress(
    PluginBaseInfo PluginInfo,
    string? Version,
    long DownloadedBytes = 0,
    long? TotalBytes = null,
    bool IsInstalling = false,
    bool IsDownloading = true)
{
    public double Percentage => IsInstalling ? 100 : TotalBytes is > 0
        ? Math.Clamp(DownloadedBytes * 100d / TotalBytes.Value, 0, 100)
        : 0;

    public bool IsIndeterminate => !IsInstalling && TotalBytes is not > 0;

    public string StatusText => IsInstalling ? "正在安装"
        : TotalBytes is > 0 ? Lang.Format("lang.kitopia.messages.downloading_value", Percentage)
        : DownloadedBytes > 0 ? Lang.Format("lang.kitopia.messages.downloading_value_mb", DownloadedBytes / 1048576d)
        : "下载中";
}
