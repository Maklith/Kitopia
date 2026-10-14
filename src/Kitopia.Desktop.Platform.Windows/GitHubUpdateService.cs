using System.Runtime.InteropServices;
using Avalonia.Controls.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using NuGet.Versioning;
using Serilog;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Feature.Localization;
using PluginCore;

namespace Kitopia.Desktop.Platform.Windows
{
    public class GitHubUpdateService(HttpClient? httpClient = null)
    {
        private static readonly ILogger Logger = LogManager.Logger.ForContext<GitHubUpdateService>();
        private static readonly HttpClient HttpClient = new() {
            DefaultRequestHeaders = { 
                { "User-Agent", $"KitopiaUpdateChecker/{ServiceManager.Version}" }
            } 
        };
        private const string Owner = "Maklith";
        private const string Repo = "kitopia";

        public async Task<(bool hasUpdate, string? latestVersion, string? downloadUrl, string? releaseNotes)> CheckForUpdatesAsync()
        {
            try
            {
                
                var url = $"https://update.kitopia.top/repos/{Owner}/{Repo}/releases";
                using var response = await (httpClient ?? HttpClient).GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    Logger.Warning($"Failed to check for updates. Status code: {response.StatusCode}");
                    _ =ServiceManager.Services.GetService<IToastService>()!.Show(Lang.Get("lang.kitopia.update"), Lang.Format("lang.kitopia.messages.cannot_check_for_updates_check_your_network_connection_code_value", response.StatusCode), NotificationType.Error);
                    return (false, null, null, null);
                }

                var json = await response.Content.ReadAsStringAsync();
                var releases = JArray.Parse(json);
                var allowPrereleaseUpdates = ConfigManger.Config?.allowPrereleaseUpdates == true;
                JToken? release = null;
                NuGetVersion? latestVersion = null;
                foreach (var candidate in releases)
                {
                    if (candidate["draft"]?.Value<bool>() == true ||
                        !allowPrereleaseUpdates && candidate["prerelease"]?.Value<bool>() == true)
                        continue;

                    var candidateTagName = candidate["tag_name"]?.ToString()?.TrimStart('v', 'V');
                    NuGetVersion.TryParse(candidateTagName, out var candidateVersion);
                    if (!allowPrereleaseUpdates && candidateVersion?.IsPrerelease == true)
                        continue;

                    release = candidate;
                    latestVersion = candidateVersion;
                    break;
                }

                if (release == null)
                {
                    if (releases.Count == 0)
                        _ = ServiceManager.Services.GetService<IToastService>()!.Show(Lang.Get("lang.kitopia.update"), Lang.Get("lang.kitopia.cannot_check_for_updates_no_release_version_found"), NotificationType.Error);
                    return (false, null, null, null);
                }

                var tagName = release["tag_name"]?.ToString();
                if (string.IsNullOrEmpty(tagName))
                {
                    _ =ServiceManager.Services.GetService<IToastService>()!.Show(Lang.Get("lang.kitopia.update"), Lang.Get("lang.kitopia.cannot_check_for_updates_no_release_version_found"), NotificationType.Error);
                    return (false, null, null, null);
                }

                var cleanTagName = tagName.TrimStart('v', 'V');
                
                if (!NuGetVersion.TryParse(ServiceManager.Version, out var currentVersion))
                {
                    Logger.Warning($"Failed to parse current version: {ServiceManager.Version }");
                    _ =ServiceManager.Services.GetService<IToastService>()!.Show(Lang.Get("lang.kitopia.update"), Lang.Get("lang.kitopia.cannot_check_for_updates_invalid_current_version"), NotificationType.Error);
                    return (false, null, null, null);
                }

                if (latestVersion is not null)
                {
                    if (VersionComparer.VersionRelease.Compare(latestVersion, currentVersion) > 0)
                    {
                        var runtime = RuntimeInformation.ProcessArchitecture switch
                        {
                            Architecture.X86 => "win-x86",
                            Architecture.X64 => "win-x64",
                            Architecture.Arm64 => "win-arm64",
                            var architecture => throw new PlatformNotSupportedException(
                                $"Unsupported update architecture: {architecture}")
                        };
                        var installerNames = new[]
                        {
                            $"Kitopia{cleanTagName}_{runtime}_Installer.exe",
                            $"Kitopia{cleanTagName}_Installer.exe"
                        };
                        var htmlUrl = (release["assets"] as JArray)?
                            .FirstOrDefault(asset => installerNames.Contains(
                                asset["name"]?.ToString(), StringComparer.OrdinalIgnoreCase))?
                            ["browser_download_url"]?.ToString();
                        if (string.IsNullOrWhiteSpace(htmlUrl))
                            Logger.Warning("No installer asset found for {Runtime} in release {Version}", runtime,
                                cleanTagName);
                        var body = release["body"]?.ToString();
                        htmlUrl = htmlUrl?.Replace("https://github.com/Maklith","https://update.kitopia.top/Maklith");
                        return (true, tagName, htmlUrl, body);
                    }
                }
                else
                {
                    Logger.Warning($"Failed to parse latest version: {cleanTagName}");
                    _ =ServiceManager.Services.GetService<IToastService>()!.Show(Lang.Get("lang.kitopia.update"), Lang.Get("lang.kitopia.cannot_check_for_updates_invalid_release_version"), NotificationType.Error);
                }
                return (false, null, null, null);
            }
            catch (Exception ex)
            {
                _ =ServiceManager.Services.GetService<IToastService>()!.Show(Lang.Get("lang.kitopia.update"), Lang.Format("lang.kitopia.messages.update_check_failed_value", ex.Message), NotificationType.Error);
                Logger.Error(ex, "Error checking for updates");
                return (false, null, null, null);
            }
            
        }
    }
}
