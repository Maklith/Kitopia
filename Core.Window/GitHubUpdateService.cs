using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading.Tasks;
using Core.Services;
using Core.Services.Plugin;
using Newtonsoft.Json.Linq;
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
                    return (false, null, null, null);
                }

                var json = await response.Content.ReadAsStringAsync();
                var releases = JArray.Parse(json);
                var release = releases.Count > 0 ? releases[0] : null;

                if (release == null)
                {
                    return (false, null, null, null);
                }

                var tagName = release["tag_name"]?.ToString();
                if (string.IsNullOrEmpty(tagName))
                {
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
                Logger.Error(ex, "Error checking for updates");
            }

            return (false, null, null, null);
        }
    }
}
