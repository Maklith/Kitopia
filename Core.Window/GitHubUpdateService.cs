using Avalonia.Controls.Notifications;
using Core.Services;
using Core.Services.Plugin;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using PluginCore;
using Serilog;

namespace Core.Window
{
    public class GitHubUpdateService
    {
        private static readonly ILogger Logger = LogManager.Logger.ForContext<GitHubUpdateService>();
        private const string Owner = "Maklith";
        private const string Repo = "kitopia";

        public async Task<(bool hasUpdate, string? latestVersion, string? downloadUrl, string? releaseNotes)> CheckForUpdatesAsync()
        {
            try
            {
                
                var url = $"https://update.kitopia.top/repos/{Owner}/{Repo}/releases";
                var response = await PluginNetworkService.HttpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    Logger.Warning($"Failed to check for updates. Status code: {response.StatusCode}");
                    _ =ServiceManager.Services.GetService<IToastService>()!.Show("更新", $"无法检查更新，请检查网络连接。\nCode: {response.StatusCode}", NotificationType.Error);
                    return (false, null, null, null);
                }

                var json = await response.Content.ReadAsStringAsync();
                var releases = JArray.Parse(json);
                var release = releases.Count > 0 ? releases[0] : null;

                if (release == null)
                {
                    _ =ServiceManager.Services .GetService<IToastService>()!.Show("更新", "无法检查更新，未找到版本信息。", NotificationType.Error);
                    return (false, null, null, null);
                }

                var tagName = release["tag_name"]?.ToString();
                if (string.IsNullOrEmpty(tagName))
                {
                    _ =ServiceManager.Services.GetService<IToastService>()!.Show("更新", "无法检查更新，未找到版本信息。", NotificationType.Error);
                    return (false, null, null, null);
                }

                // Remove 'v' prefix if present
                var cleanTagName = tagName.TrimStart('v');
                
                if (!Version.TryParse(ServiceManager.Version , out var currentVersion))
                {
                    Logger.Warning($"Failed to parse current version: {ServiceManager.Version }");
                    _ =ServiceManager.Services.GetService<IToastService>()!.Show("更新", "无法检查更新，当前版本信息格式错误。", NotificationType.Error);
                    return (false, null, null, null);
                }

                if (Version.TryParse(cleanTagName, out var latestVersion))
                {
                    if (latestVersion> currentVersion)
                    {
                        var htmlUrl = ((JArray)release["assets"]!).FirstOrDefault(e=>e["name"]!.ToString()==$"Kitopia{cleanTagName}_Installer.exe")?["browser_download_url"]?.ToString();
                        var body = release["body"]?.ToString();
                        htmlUrl = htmlUrl?.Replace("https://github.com/Maklith","https://update.kitopia.top/Maklith");
                        return (true, tagName, htmlUrl, body);
                    }
                }
                else
                {
                    Logger.Warning($"Failed to parse latest version: {cleanTagName}");
                    _ =ServiceManager.Services.GetService<IToastService>()!.Show("更新", "无法检查更新，版本信息格式错误。", NotificationType.Error);
                }
                return (false, null, null, null);
            }
            catch (Exception ex)
            {
                _ =ServiceManager.Services.GetService<IToastService>()!.Show("更新", $"检查更新时出错: {ex.Message}", NotificationType.Error);
                Logger.Error(ex, "Error checking for updates");
                return (false, null, null, null);
            }
            
        }
    }
}
