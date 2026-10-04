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
        : TotalBytes is > 0 ? $"下载中 {Percentage:F0}%"
        : DownloadedBytes > 0 ? $"下载中 {DownloadedBytes / 1048576d:F1} MB"
        : "下载中";
}
