using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Translation;
using Kitopia.Desktop.Platform.Windows;
using PluginCore;
using PluginCore.Config;

namespace KitopiaTest.Services;

[TestClass]
[DoNotParallelize]
public sealed class GitHubUpdateServiceTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CheckUpdate_IndexTranslate_TranslatesFullReleaseBodyAndFallsBackOnFailure(bool translationFails)
    {
        const string notes = "## What's Changed\n* Fix file preview selection.\n* Preserve translucent card backgrounds.\n\nFull Changelog: https://github.com/Maklith/kitopia/compare/1.0.0...1.1.0";
        const string translated = "## 更新内容\n* 修复文件预览选择。\n* 保留卡片半透明背景。\n\n完整变更：https://github.com/Maklith/kitopia/compare/1.0.0...1.1.0";
        var previousVersion = ServiceManager.Version;
        var previousServices = ServiceManager.Services;
        var previousConfigs = ConfigManger.Configs;
        var toast = new RecordingToast();
        var requests = 0;
        using var client = new HttpClient(new ReleaseResponseHandler(JsonSerializer.Serialize(new[]
        {
            new
            {
                tag_name = "1.1.0", body = notes,
                assets = new[]
                {
                    new { name = "Kitopia1.1.0_win-x64_Installer.exe", browser_download_url = "https://example.com/installer.exe" },
                    new { name = "Kitopia1.1.0_win-arm64_Installer.exe", browser_download_url = "https://example.com/installer.exe" }
                }
            }
        }))
        {
            TranslationResponse = async (request, cancellationToken) =>
            {
                requests++;
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var message = payload.RootElement.GetProperty("messages")[0].GetProperty("content").GetString();
                Assert.AreEqual($"请将以下文本翻译为简体中文，直接输出翻译结果，不要进行任何解释。\n\n{notes}", message);
                return new HttpResponseMessage(translationFails ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        choices = new[] { new { message = new { content = translated }, finish_reason = "stop" } }
                    }))
                };
            }
        });
        using var services = new ServiceCollection()
            .AddSingleton<IToastService>(toast)
            .AddSingleton(new GitHubUpdateService(client))
            .AddSingleton<IConfigService, ConfigManger>()
            .AddSingleton(new IndexTranslationService(client))
            .AddSingleton<BingTranslationService>()
            .AddSingleton<ITranslationService, TranslationService>()
            .BuildServiceProvider();
        ConfigManger.Configs = new Dictionary<string, ConfigBase>
        {
            ["KitopiaConfig"] = new KitopiaConfig { language = "zh-CN", translationProvider = TranslationProvider.IndexTranslate }
        };
        ServiceManager.Version = "1.0.0";
        ServiceManager.Services = services;
        try
        {
            await new ApplicationService().CheckUpdate(false);

            Assert.AreEqual(1, requests);
            Assert.HasCount(1, toast.Requests);
            StringAssert.Contains(toast.Requests[0].Text, translationFails ? notes : translated);
        }
        finally
        {
            ServiceManager.Version = previousVersion;
            ServiceManager.Services = previousServices;
            ConfigManger.Configs = previousConfigs;
        }
    }

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
                assets = new[]
                {
                    new
                    {
                        name = $"Kitopia{cleanTagName}_Installer.exe",
                        browser_download_url = $"https://github.com/Maklith/kitopia/releases/download/{tagName}/Kitopia{cleanTagName}_Installer.exe"
                    },
                    new { name = installerName, browser_download_url = downloadUrl }
                }
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
        var runtime = $"win-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";
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
                        name = $"Kitopia{prereleaseTag}_{runtime}_Installer.exe",
                        browser_download_url = $"https://github.com/Maklith/kitopia/releases/download/{prereleaseTag}/Kitopia{prereleaseTag}_{runtime}_Installer.exe"
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
                        name = $"Kitopia1.1.0_{runtime}_Installer.exe",
                        browser_download_url = $"https://github.com/Maklith/kitopia/releases/download/v1.1.0/Kitopia1.1.0_{runtime}_Installer.exe"
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
                    $"https://update.kitopia.top/Maklith/kitopia/releases/download/{expectedVersion}/Kitopia{expectedVersion.TrimStart('v', 'V')}_{runtime}_Installer.exe",
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

    [TestMethod]
    [DataRow(System.Runtime.InteropServices.Architecture.X64, "win-x64", false)]
    [DataRow(System.Runtime.InteropServices.Architecture.X64, "win-x64", true)]
    [DataRow(System.Runtime.InteropServices.Architecture.Arm64, "win-arm64", false)]
    [DataRow(System.Runtime.InteropServices.Architecture.Arm64, "win-arm64", true)]
    [DataRow(System.Runtime.InteropServices.Architecture.X86, "win-x86", false)]
    [DataRow(System.Runtime.InteropServices.Architecture.X86, "win-x86", true)]
    public void GetInstallerDownloadUrl_MixedAssets_PrefersMatchingArchitectureRegardlessOfOrder(
        System.Runtime.InteropServices.Architecture architecture, string runtime, bool reverseAssets)
    {
        const string version = "1.1.0-beta.1";
        string[] names =
        [
            $"Kitopia{version}_Installer.exe",
            $"Kitopia{version}_win-x64.zip",
            $"Kitopia{version}_win-arm64_Installer.exe",
            $"Kitopia{version}_win-x86_Installer.exe",
            $"Kitopia{version}_win-x64_Installer.exe"
        ];
        if (reverseAssets)
            Array.Reverse(names);
        var assets = JArray.FromObject(names.Select(name => new
        {
            name,
            browser_download_url = $"https://github.com/Maklith/kitopia/releases/download/{version}/{name}"
        }));

        var result = GitHubUpdateService.GetInstallerDownloadUrl(assets, version, architecture);

        Assert.AreEqual(
            $"https://github.com/Maklith/kitopia/releases/download/{version}/Kitopia{version}_{runtime}_Installer.exe",
            result);
    }

    [TestMethod]
    [DataRow(System.Runtime.InteropServices.Architecture.X64, true, true)]
    [DataRow(System.Runtime.InteropServices.Architecture.X64, false, false)]
    [DataRow(System.Runtime.InteropServices.Architecture.Arm64, true, false)]
    [DataRow(System.Runtime.InteropServices.Architecture.Arm64, false, false)]
    [DataRow(System.Runtime.InteropServices.Architecture.X86, true, false)]
    [DataRow(System.Runtime.InteropServices.Architecture.X86, false, false)]
    public void GetInstallerDownloadUrl_MatchingInstallerMissing_UsesLegacyOnlyForX64(
        System.Runtime.InteropServices.Architecture architecture, bool includeLegacy, bool expectedDownload)
    {
        const string version = "1.1.0";
        var otherRuntime = architecture == System.Runtime.InteropServices.Architecture.X64 ? "win-arm64" : "win-x64";
        var assets = JArray.FromObject(new[]
        {
            new
            {
                name = $"Kitopia{version}_{otherRuntime}_Installer.exe",
                browser_download_url = $"https://github.com/Maklith/kitopia/releases/download/{version}/Kitopia{version}_{otherRuntime}_Installer.exe"
            }
        });
        const string legacyUrl = "https://github.com/Maklith/kitopia/releases/download/1.1.0/Kitopia1.1.0_Installer.exe";
        if (includeLegacy)
            assets.Add(JObject.FromObject(new { name = $"Kitopia{version}_Installer.exe", browser_download_url = legacyUrl }));

        var result = GitHubUpdateService.GetInstallerDownloadUrl(assets, version, architecture);

        Assert.AreEqual(expectedDownload ? legacyUrl : null, result);
    }

    private sealed class ReleaseResponseHandler(string json) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? TranslationResponse { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host == "index-translate.bilibili.com" && TranslationResponse is not null)
                return TranslationResponse(request, cancellationToken);
            Assert.AreEqual("https://update.kitopia.top/repos/Maklith/kitopia/releases", request.RequestUri?.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }

    private sealed class RecordingToast : IToastService
    {
        public List<NotificationType> Notifications { get; } = [];
        public List<ToastRequest> Requests { get; } = [];
        public void Init() { }
        public Task Show(string header, string text, NotificationType notificationType = NotificationType.Information,
            Window? dialogWindow = null)
        {
            Notifications.Add(notificationType);
            return Task.CompletedTask;
        }
        public Task Show(ToastRequest request, Window? dialogWindow = null)
        {
            Requests.Add(request);
            return Show(request.Header, request.Text, request.NotificationType, dialogWindow);
        }
        public IToastProgressHandle ShowProgress(string header, string text, NotificationType notificationType,
            double initialProgress = 0, bool isIndeterminate = false) => throw new NotSupportedException();
        public bool HasUnreadSuppressedNotifications() => false;
        public bool TryOpenLatestSuppressedNotification() => false;
        public bool ShowSuppressedNotificationCenter() => false;
        public void ClearUnreadSuppressedNotifications() { }
        public void Unregister() { }
    }
}
