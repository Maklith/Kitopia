using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Kitopia.Desktop.Platform.Windows;
using PluginCore;

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
