using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Platform.Windows;
using PluginCore;
using PluginCore.Config;

namespace KitopiaTest.Services;

[TestClass]
[DoNotParallelize]
public sealed class GitHubUpdateServiceTests
{
    [TestMethod]
    [DataRow("0.3.8.0", "v1.0.0-beta.1", true, false)]
    [DataRow("1.0.0-beta.1", "1.0.0-beta.2", true, false)]
    [DataRow("1.0.0-beta.2", "1.0.0-beta.10", true, false)]
    [DataRow("1.0.0-beta.10", "1.0.0-beta.2", false, false)]
    [DataRow("1.0.0-beta.1", "V1.0.0-rc.1", true, false)]
    [DataRow("1.0.0-rc.1", "1.0.0", true, false)]
    [DataRow("1.0.0", "1.0.0-beta.2", false, false)]
    [DataRow("1.0.0-beta.1", "v1.0.0-beta.1", false, false)]
    [DataRow("1.0.0-beta.1+build.1", "1.0.0-beta.1+build.2", false, false)]
    [DataRow("1.0.0-beta.1+build.1", "1.0.0-beta.2+build.2", true, false)]
    [DataRow("1.0.0.0", "1.0.0", false, false)]
    [DataRow("0.3.8.0", "0.3.8.1", true, false)]
    [DataRow("invalid", "1.0.0-beta.1", false, true)]
    [DataRow("1.0.0-beta.1", "invalid", false, true)]
    public async Task CheckForUpdatesAsync_ReleaseVersion_UsesSemanticPrecedenceAndPreservesInstallerName(
        string currentVersion, string tagName, bool expectedUpdate, bool expectedError)
    {
        var previousVersion = ServiceManager.Version;
        var previousServices = ServiceManager.Services;
        var previousConfigs = ConfigManger.Configs;
        var toast = new RecordingToast();
        using var services = new ServiceCollection().AddSingleton<IToastService>(toast).BuildServiceProvider();
        var cleanTagName = tagName.TrimStart('v', 'V');
        var runtime = RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X86 => "win-x86",
            System.Runtime.InteropServices.Architecture.X64 => "win-x64",
            System.Runtime.InteropServices.Architecture.Arm64 => "win-arm64",
            _ => throw new PlatformNotSupportedException()
        };
        var installerName = $"Kitopia{cleanTagName}_{runtime}_Installer.exe";
        var downloadUrl = $"https://github.com/Maklith/kitopia/releases/download/{tagName}/{installerName}";
        var json = JsonSerializer.Serialize(new[]
        {
            new
            {
                tag_name = tagName,
                body = "Release notes",
                assets = new[] { new { name = installerName, browser_download_url = downloadUrl } }
            }
        });
        using var client = new HttpClient(new ReleaseResponseHandler(json));
        ConfigManger.Configs = new Dictionary<string, ConfigBase>
        {
            ["KitopiaConfig"] = new KitopiaConfig { allowPrereleaseUpdates = true }
        };
        ServiceManager.Version = currentVersion;
        ServiceManager.Services = services;

        try
        {
            var result = await new GitHubUpdateService(client).CheckForUpdatesAsync();

            Assert.AreEqual(expectedUpdate, result.hasUpdate);
            Assert.AreEqual(expectedError ? 1 : 0, toast.Notifications.Count);
            if (expectedError)
                Assert.AreEqual(NotificationType.Error, toast.Notifications[0]);

            if (expectedUpdate)
            {
                Assert.AreEqual(tagName, result.latestVersion);
                Assert.AreEqual(
                    $"https://update.kitopia.top/Maklith/kitopia/releases/download/{tagName}/{installerName}",
                    result.downloadUrl);
                Assert.AreEqual("Release notes", result.releaseNotes);
            }
            else
            {
                Assert.IsNull(result.latestVersion);
                Assert.IsNull(result.downloadUrl);
                Assert.IsNull(result.releaseNotes);
            }
        }
        finally
        {
            ServiceManager.Version = previousVersion;
            ServiceManager.Services = previousServices;
            ConfigManger.Configs = previousConfigs;
        }
    }

    [TestMethod]
    [DataRow(false, true, "2.0.0-beta.1", "1.0.0", true, "v1.1.0")]
    [DataRow(true, true, "2.0.0-beta.1", "1.0.0", true, "2.0.0-beta.1")]
    [DataRow(false, false, "2.0.0-rc.1", "1.0.0", true, "v1.1.0")]
    [DataRow(true, false, "2.0.0-rc.1", "1.0.0", true, "2.0.0-rc.1")]
    [DataRow(false, true, "2.0.0", "1.0.0", true, "v1.1.0")]
    [DataRow(true, true, "2.0.0", "1.0.0", true, "2.0.0")]
    [DataRow(false, true, "2.0.0-beta.1", "1.0.0", false, null)]
    [DataRow(false, false, "2.0.0-beta.1", "1.0.0", false, null)]
    [DataRow(true, true, "2.0.0-beta.1", "1.0.0", false, "2.0.0-beta.1")]
    [DataRow(false, true, "2.0.0-beta.1", "1.1.0", true, null)]
    [DataRow(false, true, "2.0.0-beta.1", "1.1.0-rc.1", true, "v1.1.0")]
    [DataRow(false, true, "2.0.0-beta.1", "1.2.0-beta.1", true, null)]
    [DataRow(null, false, "2.0.0-beta.1", "1.0.0", true, "v1.1.0")]
    public async Task CheckForUpdatesAsync_PrereleasePreference_SelectsFirstEligiblePublishedRelease(
        bool? allowPrereleaseUpdates, bool githubPrerelease, string prereleaseTag, string currentVersion,
        bool includeStableRelease, string? expectedVersion)
    {
        var previousVersion = ServiceManager.Version;
        var previousServices = ServiceManager.Services;
        var previousConfigs = ConfigManger.Configs;
        var toast = new RecordingToast();
        using var services = new ServiceCollection().AddSingleton<IToastService>(toast).BuildServiceProvider();
        var releases = new List<object>
        {
            new { tag_name = "3.0.0", draft = true, prerelease = false },
            new
            {
                tag_name = prereleaseTag,
                prerelease = githubPrerelease,
                assets = new[]
                {
                    new
                    {
                        name = $"Kitopia{prereleaseTag}_Installer.exe",
                        browser_download_url = $"https://github.com/Maklith/kitopia/releases/download/{prereleaseTag}/Kitopia{prereleaseTag}_Installer.exe"
                    }
                }
            }
        };
        if (includeStableRelease)
        {
            releases.Add(new
            {
                tag_name = "v1.1.0",
                prerelease = false,
                assets = new[]
                {
                    new
                    {
                        name = "Kitopia1.1.0_Installer.exe",
                        browser_download_url = "https://github.com/Maklith/kitopia/releases/download/v1.1.0/Kitopia1.1.0_Installer.exe"
                    }
                }
            });
        }
        using var client = new HttpClient(new ReleaseResponseHandler(JsonSerializer.Serialize(releases)));
        ConfigManger.Configs = allowPrereleaseUpdates.HasValue
            ? new Dictionary<string, ConfigBase>
            {
                ["KitopiaConfig"] = new KitopiaConfig { allowPrereleaseUpdates = allowPrereleaseUpdates.Value }
            }
            : new Dictionary<string, ConfigBase>();
        ServiceManager.Version = currentVersion;
        ServiceManager.Services = services;
        try
        {
            var result = await new GitHubUpdateService(client).CheckForUpdatesAsync();

            Assert.AreEqual(expectedVersion is not null, result.hasUpdate);
            Assert.AreEqual(expectedVersion, result.latestVersion);
            Assert.IsEmpty(toast.Notifications);
            if (expectedVersion is not null)
                Assert.AreEqual(
                    $"https://update.kitopia.top/Maklith/kitopia/releases/download/{expectedVersion}/Kitopia{expectedVersion.TrimStart('v', 'V')}_Installer.exe",
                    result.downloadUrl);
            else
                Assert.IsNull(result.downloadUrl);
        }
        finally
        {
            ServiceManager.Version = previousVersion;
            ServiceManager.Services = previousServices;
            ConfigManger.Configs = previousConfigs;
        }
    }

    private sealed class ReleaseResponseHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.AreEqual("https://update.kitopia.top/repos/Maklith/kitopia/releases", request.RequestUri?.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }

    private sealed class RecordingToast : IToastService
    {
        public List<NotificationType> Notifications { get; } = [];
        public void Init() { }
        public Task Show(string header, string text, NotificationType notificationType = NotificationType.Information,
            Window? dialogWindow = null)
        {
            Notifications.Add(notificationType);
            return Task.CompletedTask;
        }
        public Task Show(ToastRequest request, Window? dialogWindow = null) =>
            Show(request.Header, request.Text, request.NotificationType, dialogWindow);
        public IToastProgressHandle ShowProgress(string header, string text, NotificationType notificationType,
            double initialProgress = 0, bool isIndeterminate = false) => throw new NotSupportedException();
        public bool HasUnreadSuppressedNotifications() => false;
        public bool TryOpenLatestSuppressedNotification() => false;
        public bool ShowSuppressedNotificationCenter() => false;
        public void ClearUnreadSuppressedNotifications() { }
        public void Unregister() { }
    }
}
