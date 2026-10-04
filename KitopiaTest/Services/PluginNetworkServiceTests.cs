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
