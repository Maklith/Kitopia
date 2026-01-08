using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Core.Services;
using Core.Services.Plugin;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using PluginCore;
using Serilog;

namespace KitopiaAvalonia.Services
{
    public class GitHubUpdateService
    {
        private static ILogger Logger = LogManager.Logger.ForContext<GitHubUpdateService>();
        private const string Owner = "Maklith";
        private const string Repo = "kitopia";
        private const string UserAgent = "KitopiaUpdateChecker";

        public async Task<(bool hasUpdate, string? latestVersion, string? downloadUrl, string? releaseNotes)> CheckForUpdatesAsync()
        {
            try
            {
                
                var url = $"https://update.kitopia.top/repos/{Owner}/{Repo}/releases";
                var response = await PluginNetworkService.HttpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    Logger.Warning($"Failed to check for updates. Status code: {response.StatusCode}");
                    ServiceManager.Services.GetService<IToastService>()!.Show("更新", $"无法检查更新，请检查网络连接。\nCode: {response.StatusCode}", NotificationType.Error);
                    return (false, null, null, null);
                }

                var json = await response.Content.ReadAsStringAsync();
                var releases = JArray.Parse(json);
                var release = releases.Count > 0 ? releases[0] : null;

                if (release == null)
                {
                    ServiceManager.Services .GetService<IToastService>()!.Show("更新", "无法检查更新，未找到版本信息。", NotificationType.Error);
                    return (false, null, null, null);
                }

                var tagName = release["tag_name"]?.ToString();
                if (string.IsNullOrEmpty(tagName))
                {
                    ServiceManager.Services.GetService<IToastService>()!.Show("更新", "无法检查更新，未找到版本信息。", NotificationType.Error);
                    return (false, null, null, null);
                }

                // Remove 'v' prefix if present
                var cleanTagName = tagName.TrimStart('v');
                
                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
                
                // If current version is not set (e.g. 0.0.0.0), assume dev build and skip update check or handle differently
                // For now, we proceed with standard comparison.

                if (Version.TryParse(cleanTagName, out var latestVersion))
                {
                    if (latestVersion> currentVersion)
                    {
                        var htmlUrl = ((JArray)release["assets"]).FirstOrDefault(e=>e["name"].ToString()==$"Kitopia{cleanTagName}_Installer.exe")?["browser_download_url"]?.ToString();
                        var body = release["body"]?.ToString();
                        htmlUrl = htmlUrl?.Replace("https://github.com/Maklith","https://update.kitopia.top/Maklith");
                        return (true, tagName, htmlUrl, body);
                    }
                }
            }
            catch (Exception ex)
            {
                ServiceManager.Services.GetService<IToastService>()!.Show("更新", $"检查更新时出错: {ex.Message}", NotificationType.Error);
                Logger.Error(ex, "Error checking for updates");
            }

            return (false, null, null, null);
        }
    }
}
