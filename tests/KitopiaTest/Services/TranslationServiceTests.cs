using System.Net;
using System.Text.Json;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Translation;
using KitopiaEx.Translate;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace KitopiaTest.Services;

[TestClass]
public sealed class TranslationServiceTests
{
    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"ConfigVersion\":4}")]
    public void TranslationProvider_MissingFromConfig_DefaultsToIndexTranslate(string json)
    {
        Assert.AreEqual(TranslationProvider.IndexTranslate, new KitopiaConfig().translationProvider);
        var config = JsonSerializer.Deserialize<KitopiaConfig>(json, ConfigManger.DefaultOptions)!;
        Assert.AreEqual(TranslationProvider.IndexTranslate, config.translationProvider);

        config.translationProvider = TranslationProvider.Microsoft;
        var restored = JsonSerializer.Deserialize<KitopiaConfig>(
            JsonSerializer.Serialize(config, ConfigManger.DefaultOptions), ConfigManger.DefaultOptions)!;
        Assert.AreEqual(TranslationProvider.Microsoft, restored.translationProvider);
    }

    [TestMethod]
    [DataRow(TranslationSourceLanguage.Auto, TranslationTargetLanguage.SimplifiedChinese, "", "简体中文")]
    [DataRow(TranslationSourceLanguage.SimplifiedChinese, TranslationTargetLanguage.TraditionalChinese, "简体中文", "繁体中文")]
    [DataRow(TranslationSourceLanguage.TraditionalChinese, TranslationTargetLanguage.English, "繁体中文", "英语")]
    [DataRow(TranslationSourceLanguage.English, TranslationTargetLanguage.Japanese, "英语", "日语")]
    [DataRow(TranslationSourceLanguage.Japanese, TranslationTargetLanguage.SimplifiedChinese, "日语", "简体中文")]
    public async Task IndexTranslate_Request_UsesOfficialApiAndLanguageInstructions(
        TranslationSourceLanguage source, TranslationTargetLanguage target, string sourceName, string targetName)
    {
        const string text = "line 1 & + = \"quoted\"\n中文";
        using var client = new HttpClient(new TestHandler(async (request, cancellationToken) =>
        {
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual("https://index-translate.bilibili.com/v1/chat/completions", request.RequestUri!.AbsoluteUri);
            Assert.AreEqual("Index-Translate-Client/1.0", request.Headers.UserAgent.ToString());
            Assert.IsNull(request.Headers.Authorization);
            Assert.AreEqual("application/json", request.Content!.Headers.ContentType!.MediaType);
            using var document = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            var payload = document.RootElement;
            Assert.AreEqual("Index-Translate-35B-A3B", payload.GetProperty("model").GetString());
            Assert.AreEqual(0, payload.GetProperty("temperature").GetInt32());
            Assert.AreEqual(4096, payload.GetProperty("max_tokens").GetInt32());
            Assert.IsFalse(payload.GetProperty("stream").GetBoolean());
            Assert.IsFalse(payload.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
            var messages = payload.GetProperty("messages");
            Assert.AreEqual(1, messages.GetArrayLength());
            Assert.AreEqual("user", messages[0].GetProperty("role").GetString());
            Assert.AreEqual($"请将以下{sourceName}文本翻译为{targetName}，直接输出翻译结果，不要进行任何解释。\n\n{text}",
                messages[0].GetProperty("content").GetString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":"translated"},"finish_reason":"stop"}]}""")
            };
        }));

        Assert.AreEqual("translated", await new IndexTranslationService(client).TranslateAsync(text, source, target));
    }

    [TestMethod]
    [DataRow("translated")]
    [DataRow("<think></think>\n translated ")]
    [DataRow("<think>reasoning</think>translated")]
    [DataRow("reasoning</think>translated")]
    public async Task IndexTranslate_ThinkingBlock_ReturnsOnlyTranslation(string content)
    {
        using var client = new HttpClient(new TestHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } }))
        })));

        Assert.AreEqual("translated", await new IndexTranslationService(client).TranslateAsync("hello"));
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("{}")]
    [DataRow("{\"error\":{\"message\":\"unavailable\"}}")]
    [DataRow("{\"choices\":[]}")]
    [DataRow("{\"choices\":[null]}")]
    [DataRow("{\"choices\":[{\"message\":{\"content\":null}}]}")]
    [DataRow("{\"choices\":[{\"message\":{\"content\":\" \"}}]}")]
    [DataRow("{\"choices\":[{\"message\":{\"content\":\"<think>reasoning\"}}]}")]
    [DataRow("{\"choices\":[{\"message\":{\"content\":\"<think>reasoning</think>\"}}]}")]
    public async Task IndexTranslate_InvalidResponse_ThrowsInsteadOfReturningErrorOrReasoning(string response)
    {
        using var client = new HttpClient(new TestHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response)
        })));

        await Assert.ThrowsExactlyAsync<JsonException>(() => new IndexTranslationService(client).TranslateAsync("hello"));
    }

    [TestMethod]
    public async Task IndexTranslate_TruncatedOutput_ThrowsInsteadOfReturningPartialTranslation()
    {
        using var client = new HttpClient(new TestHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"choices":[{"message":{"content":"partial"},"finish_reason":"length"}]}""")
        })));

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => new IndexTranslationService(client).TranslateAsync("hello"));
    }

    [TestMethod]
    [DataRow(HttpStatusCode.Forbidden)]
    [DataRow(HttpStatusCode.TooManyRequests)]
    [DataRow(HttpStatusCode.InternalServerError)]
    public async Task IndexTranslate_HttpError_PropagatesStatusWithoutRetry(HttpStatusCode status)
    {
        var requests = 0;
        using var client = new HttpClient(new TestHandler((_, _) =>
        {
            requests++;
            return Task.FromResult(new HttpResponseMessage(status));
        }));

        var exception = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => new IndexTranslationService(client).TranslateAsync("hello"));

        Assert.AreEqual(status, exception.StatusCode);
        Assert.AreEqual(1, requests);
    }

    [TestMethod]
    public async Task IndexTranslate_BlankOrCancelledInput_DoesNotSendRequest()
    {
        using var client = new HttpClient(new TestHandler((_, _) => throw new AssertFailedException("Unexpected HTTP request.")));
        var service = new IndexTranslationService(client);
        Assert.AreEqual(" \n", await service.TranslateAsync(" \n"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.TranslateAsync("hello", cancellationToken: cancellation.Token));
    }

    [TestMethod]
    public async Task IndexTranslate_CancelledDuringRequest_PropagatesCancellation()
    {
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new TestHandler(async (_, cancellationToken) =>
        {
            requested.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new AssertFailedException("Cancelled requests must not complete.");
        }));
        using var cancellation = new CancellationTokenSource();
        var task = new IndexTranslationService(client).TranslateAsync("hello", cancellationToken: cancellation.Token);
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [TestMethod]
    public async Task Translate_ProviderChanges_UsesNewSelectionOnNextCall()
    {
        var config = new TestConfigService();
        var indexRequests = 0;
        var microsoftRequests = 0;
        using var indexClient = new HttpClient(new TestHandler((_, _) =>
        {
            indexRequests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":"index"}}]}""")
            });
        }));
        using var microsoftClient = new HttpClient(new TestHandler(async (request, cancellationToken) =>
        {
            microsoftRequests++;
            if (request.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        <div id="rich_tta" data-iid="translator.5023"></div>
                        <script>var _G = {IG:"test"}; var params_AbusePreventionHelper = [1791197251895,"token",3600000];</script>
                        """)
                };
            var form = System.Web.HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.AreEqual("hello", form["text"]);
            Assert.AreEqual("en", form["fromLang"]);
            Assert.AreEqual("zh-Hant", form["to"]);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""[{"translations":[{"text":"microsoft"}]}]""")
            };
        }));
        var service = new TranslationService(config, new IndexTranslationService(indexClient), new BingTranslationService(microsoftClient));
        Assert.AreEqual("index", await service.TranslateAsync("hello"));
        config.Config.translationProvider = TranslationProvider.Microsoft;
        Assert.AreEqual("microsoft", await service.TranslateAsync("hello", TranslationSourceLanguage.English, TranslationTargetLanguage.TraditionalChinese));
        config.Config.translationProvider = TranslationProvider.IndexTranslate;
        Assert.AreEqual("index", await service.TranslateAsync("hello"));
        Assert.AreEqual(2, indexRequests);
        Assert.AreEqual(2, microsoftRequests);
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task TranslatePluginNode_HostServiceAvailable_UsesConfiguredProvider()
    {
        var previousServices = PluginCore.Kitopia.ServiceProvider;
        var requests = 0;
        using var client = new HttpClient(new TestHandler(async (request, cancellationToken) =>
        {
            requests++;
            Assert.AreEqual("index-translate.bilibili.com", request.RequestUri!.Host);
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            StringAssert.Contains(payload.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!, "日语文本翻译为繁体中文");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":"translated"}}]}""")
            };
        }));
        using var services = new ServiceCollection()
            .AddSingleton<IConfigService, TestConfigService>()
            .AddSingleton(new IndexTranslationService(client))
            .AddSingleton<BingTranslationService>()
            .AddSingleton<ITranslationService, TranslationService>()
            .BuildServiceProvider();
        try
        {
            PluginCore.Kitopia.ServiceProvider = services;
            var node = new KitopiaEx.CustomScenarioMethods.Translate();
            Assert.AreEqual("translated", await node.TranslateOcrResults("source", SourceTranslateLang.日本語,
                TargetTranslateLang.繁體中文, CancellationToken.None));
            Assert.AreEqual(1, requests);
        }
        finally
        {
            PluginCore.Kitopia.ServiceProvider = previousServices;
        }
    }

    [TestMethod]
    [TestCategory("TranslationNetwork")]
    public async Task IndexTranslate_LiveService_ReturnsChineseText()
    {
        if (Environment.GetEnvironmentVariable("KITOPIA_TRANSLATION_LIVE_TEST") != "1")
            Assert.Inconclusive("Enable KITOPIA_TRANSLATION_LIVE_TEST to run the network smoke test.");

        var translated = await new IndexTranslationService().TranslateAsync("Hello world", TranslationSourceLanguage.English);
        Assert.IsFalse(string.IsNullOrWhiteSpace(translated));
        Assert.IsTrue(translated.Any(character => character is >= '\u4e00' and <= '\u9fff'));
        Console.WriteLine(translated);
    }

    private sealed class TestConfigService : IConfigService
    {
        public KitopiaConfig Config { get; } = new();
    }

    private sealed class TestHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }
}
