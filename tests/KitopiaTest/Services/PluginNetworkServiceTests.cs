using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using CpuArchitecture = System.Runtime.InteropServices.Architecture;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Plugin;
using PluginCore.Config;

namespace KitopiaTest.Services;

[TestClass]
[DoNotParallelize]
public sealed class PluginNetworkServiceTests
{
    [TestMethod]
    public async Task FetchPluginAvatarAsync_PngResponseReturnsOriginalBytesWithAuthorizedRequest()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
        using var handler = new AvatarResponseHandler(HttpStatusCode.OK, png);
        using var client = new HttpClient(handler);
        var previousConfigs = ConfigManger.Configs;
        try
        {
            ConfigManger.Configs = new Dictionary<string, ConfigBase>
            {
                ["KitopiaConfig"] = new KitopiaConfig { userToken = "avatar-test-token" }
            };

            var result = await PluginNetworkService.FetchPluginAvatarAsync("kitopiaonnxruntimecuda", client);

            CollectionAssert.AreEqual(png, result);
            Assert.AreEqual("/api/v1/plugin/avatar?namesign=kitopiaonnxruntimecuda", handler.RequestUri?.PathAndQuery);
            Assert.AreEqual("Bearer avatar-test-token", handler.Authorization);
        }
        finally
        {
            ConfigManger.Configs = previousConfigs;
        }
    }

    [TestMethod]
    [DataRow(HttpStatusCode.NotFound)]
    [DataRow(HttpStatusCode.Forbidden)]
    [DataRow(HttpStatusCode.InternalServerError)]
    public async Task FetchPluginAvatarAsync_FailedResponseReturnsNoImage(HttpStatusCode statusCode)
    {
        using var client = new HttpClient(new AvatarResponseHandler(statusCode, [0x89, 0x50, 0x4e, 0x47]));

        Assert.IsNull(await PluginNetworkService.FetchPluginAvatarAsync("missing", client));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task DownloadArchiveAsync_StreamingResponse_ReportsReceivedBytes(bool hasContentLength)
    {
        var bytes = Enumerable.Range(0, 16384).Select(index => (byte)index).ToArray();
        using var client = new HttpClient(new DownloadResponseHandler(bytes, hasContentLength));
        var archive = Path.Combine(Path.GetTempPath(), $"kitopia-download-{Guid.NewGuid():N}.zip");
        var reports = new List<(long Downloaded, long? Total)>();
        try
        {
            await PluginNetworkService.DownloadArchiveAsync("progress_test", "1.0.0", archive, client,
                (downloaded, total) => reports.Add((downloaded, total)), CancellationToken.None);

            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(archive));
            Assert.AreEqual(0L, reports[0].Downloaded);
            Assert.AreEqual(hasContentLength ? (long?)bytes.Length : null, reports[0].Total);
            Assert.IsTrue(reports.Any(report => report.Downloaded > 0 && report.Downloaded < bytes.Length));
            Assert.AreEqual((bytes.LongLength, (long?)bytes.Length), reports[^1]);
            Assert.IsTrue(reports.Zip(reports.Skip(1)).All(pair => pair.First.Downloaded <= pair.Second.Downloaded));
        }
        finally
        {
            File.Delete(archive);
        }
    }

    [TestMethod]
    public async Task DownloadArchiveAsync_CanceledDuringDownload_DisposesArchiveWithoutCompletingProgress()
    {
        using var client = new HttpClient(new DownloadResponseHandler(new byte[16384], true));
        using var cancellation = new CancellationTokenSource();
        var archive = Path.Combine(Path.GetTempPath(), $"kitopia-download-{Guid.NewGuid():N}.zip");
        long lastDownloaded = 0;
        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => PluginNetworkService.DownloadArchiveAsync(
                "progress_test", "1.0.0", archive, client, (downloaded, _) =>
                {
                    lastDownloaded = downloaded;
                    if (downloaded > 0) cancellation.Cancel();
                }, cancellation.Token));
            Assert.IsTrue(lastDownloaded > 0 && lastDownloaded < 16384);
        }
        finally
        {
            File.Delete(archive);
        }
    }

    [TestMethod]
    public async Task DownloadArchiveAsync_FailedResponse_DoesNotCreateArchiveOrReportProgress()
    {
        using var client = new HttpClient(new AvatarResponseHandler(HttpStatusCode.BadGateway, []));
        var archive = Path.Combine(Path.GetTempPath(), $"kitopia-download-{Guid.NewGuid():N}.zip");
        try
        {
            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => PluginNetworkService.DownloadArchiveAsync(
                "progress_test", "1.0.0", archive, client, (_, _) => Assert.Fail("Failed requests must not report progress."),
                CancellationToken.None));
            Assert.IsFalse(File.Exists(archive));
        }
        finally
        {
            File.Delete(archive);
        }
    }

    [TestMethod]
    [DataRow(true, false, false, CpuArchitecture.X64, "win-x64")]
    [DataRow(true, false, false, CpuArchitecture.Arm64, "win-arm64")]
    [DataRow(false, true, false, CpuArchitecture.X64, "osx-x64")]
    [DataRow(false, true, false, CpuArchitecture.Arm64, "osx-arm64")]
    [DataRow(false, false, true, CpuArchitecture.X64, "linux-x64")]
    [DataRow(false, false, true, CpuArchitecture.Arm64, "linux-arm64")]
    public void GetRuntimeIdentifier_MapsOsAndArchitectureToCanonicalTargets(
        bool isWindows,
        bool isMacOS,
        bool isLinux,
        CpuArchitecture architecture,
        string expected)
    {
        Assert.AreEqual(expected, PluginNetworkService.GetRuntimeIdentifier(isWindows, isMacOS, isLinux, architecture));
    }

    [TestMethod]
    [DataRow(new[] { "win-x64" }, "win-x64", false, true)]
    [DataRow(new[] { "win-x64" }, "win-arm64", false, false)]
    [DataRow(new[] { "win-arm64" }, "win-arm64", false, true)]
    [DataRow(new[] { "win-x64", "win-arm64" }, "win-arm64", false, true)]
    [DataRow(new[] { "windows" }, "win-x64", false, true)]
    [DataRow(new[] { "windows" }, "win-arm64", false, false)]
    [DataRow(new[] { "macos" }, "osx-x64", false, true)]
    [DataRow(new[] { "macos" }, "osx-arm64", false, false)]
    [DataRow(new[] { "linux" }, "linux-x64", false, true)]
    [DataRow(new[] { "linux" }, "linux-arm64", false, false)]
    [DataRow(new[] { "4" }, "win-arm64", false, true)]
    [DataRow(new string[0], "win-x64", false, false)]
    [DataRow(new string[0], "win-x64", true, true)]
    [DataRow(new[] { "freebsd-x64" }, "win-x64", true, false)]
    public void SupportsRuntimeIdentifier_EnforcesExactArchitectureAndLegacyRules(
        string[] platforms,
        string currentRid,
        bool allowEmpty,
        bool expected)
    {
        Assert.AreEqual(expected, PluginNetworkService.SupportsRuntimeIdentifier(platforms, currentRid, allowEmpty));
    }

    [TestMethod]
    [DataRow("win-x64", "Windows x64")]
    [DataRow("win-arm64", "Windows ARM64")]
    [DataRow("osx-x64", "macOS x64")]
    [DataRow("osx-arm64", "macOS ARM64")]
    [DataRow("linux-x64", "Linux x64")]
    [DataRow("linux-arm64", "Linux ARM64")]
    [DataRow("windows", "Windows x64")]
    [DataRow("macos", "macOS x64")]
    [DataRow("linux", "Linux x64")]
    [DataRow("freebsd-x64", "freebsd-x64")]
    public void FormatPlatformName_FormatsCanonicalAndLegacyIdentifiers(string platform, string expected)
    {
        Assert.AreEqual(expected, PluginInfoUiHelper.FormatPlatformName(platform));
    }

    [TestMethod]
    public async Task DownloadArchiveAsync_RequestsCurrentRuntimeIdentifierRoute()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        using var handler = new AvatarResponseHandler(HttpStatusCode.OK, bytes);
        using var client = new HttpClient(handler);
        var archive = Path.Combine(Path.GetTempPath(), $"kitopia-rid-{Guid.NewGuid():N}.zip");
        try
        {
            await PluginNetworkService.DownloadArchiveAsync(
                "demo", "1.2.3", archive, client, null, CancellationToken.None);
            Assert.AreEqual(
                $"/api/v1/plugin/download/{PluginNetworkService.GetCurrentRuntimeIdentifier()}/demo/1.2.3",
                handler.RequestUri?.PathAndQuery);
        }
        finally
        {
            File.Delete(archive);
        }
    }

    [TestMethod]
    public async Task GetLatestCompatibleVersionAsync_SkipsNewerIncompatibleAndDraftReleases()
    {
        const string payload = """
            {
              "flag": true,
              "data": [
                { "id": 3, "version": "3.0.0", "availablePlatforms": ["win-arm64"], "canDownload": false },
                { "id": 2, "version": "2.0.0", "availablePlatforms": ["win-x64"], "canDownload": true },
                { "id": 1, "version": "1.2.0", "availablePlatforms": ["win-arm64"], "canDownload": true },
                { "id": 0, "version": "1.1.0", "availablePlatforms": ["win-arm64"], "canDownload": true }
              ]
            }
            """;
        using var handler = new JsonResponseHandler(HttpStatusCode.OK, payload);
        using var client = new HttpClient(handler);

        var latest = await PluginNetworkService.GetLatestCompatibleVersionAsync("demo", "win-arm64", client);

        Assert.AreEqual("1.2.0", latest);
        Assert.AreEqual("/api/v1/plugin/versions/win-arm64/demo", handler.RequestUri?.PathAndQuery);
    }

    [TestMethod]
    public void VersionDetail_CanDownloadOnCurrentPlatform_RequiresMatchingArchitecture()
    {
        var currentRid = PluginNetworkService.GetCurrentRuntimeIdentifier();
        var incompatibleRid = currentRid == "win-arm64" ? "win-x64" : "win-arm64";

        Assert.IsTrue(new VersionDetail
        {
            Version = "1.0.0",
            CanDownload = true,
            AvailablePlatforms = [currentRid]
        }.CanDownloadOnCurrentPlatform);

        Assert.IsFalse(new VersionDetail
        {
            Version = "1.0.0",
            CanDownload = true,
            AvailablePlatforms = [incompatibleRid]
        }.CanDownloadOnCurrentPlatform);

        Assert.IsFalse(new VersionDetail
        {
            Version = "1.0.0",
            CanDownload = true,
            AvailablePlatforms = []
        }.CanDownloadOnCurrentPlatform);

        Assert.IsFalse(new VersionDetail
        {
            Version = "1.0.0",
            CanDownload = false,
            AvailablePlatforms = [currentRid]
        }.CanDownloadOnCurrentPlatform);
    }

    private sealed class DownloadResponseHandler(byte[] bytes, bool hasContentLength) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new StreamContent(new DownloadStream(bytes));
            if (hasContentLength) content.Headers.ContentLength = bytes.Length;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class DownloadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(110, cancellationToken);
            return await base.ReadAsync(buffer[..Math.Min(buffer.Length, 4096)], cancellationToken);
        }
    }

    private sealed class AvatarResponseHandler(HttpStatusCode statusCode, byte[] bytes) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return Task.FromResult(new HttpResponseMessage(statusCode) { Content = content });
        }
    }

    private sealed class JsonResponseHandler(HttpStatusCode statusCode, string json) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
