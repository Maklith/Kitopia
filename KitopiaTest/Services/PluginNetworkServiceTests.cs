using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
}
