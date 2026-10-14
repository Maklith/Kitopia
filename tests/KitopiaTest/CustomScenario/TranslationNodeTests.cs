using System.Net;
using System.Reflection;
using System.Text.Json;
using Kitopia.Desktop.Features.CustomScenario;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Features.Utils;
using KitopiaEx.Translate;
using Microsoft.Extensions.DependencyInjection;
using PluginCore.CustomScenario;
using PluginCore.CustomScenario.Attribute.Scenario;

namespace KitopiaTest.CustomScenario;

[TestClass]
[DoNotParallelize]
public sealed class TranslationNodeTests
{
    private static readonly FieldInfo ClientField = typeof(TranslateApi).GetField("httpClient", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly FieldInfo SessionField = typeof(TranslateApi).GetField("session", BindingFlags.NonPublic | BindingFlags.Static)!;
    private object? _previousClient;
    private object? _previousToken;
    private HttpClient? _client;

    [TestInitialize]
    public void Initialize()
    {
        _previousClient = ClientField.GetValue(null);
        _previousToken = SessionField.GetValue(null);
        SessionField.SetValue(null, null);
    }

    [TestCleanup]
    public void Cleanup()
    {
        ClientField.SetValue(null, _previousClient);
        SessionField.SetValue(null, _previousToken);
        _client?.Dispose();
    }

    [TestMethod]
    public async Task Translate_AuthenticationExpired_UsesRefreshedTokenOnSingleRetry()
    {
        var posts = 0;
        var authenticationRequests = 0;
        UseHandler(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                authenticationRequests++;
                Assert.IsNull(request.Headers.Authorization);
                return AuthenticationPage(request, authenticationRequests == 1 ? "old-token" : "fresh-token");
            }
            posts++;
            Assert.AreEqual("cn.bing.com", request.RequestUri!.Host);
            var form = System.Web.HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.AreEqual("auto-detect", form["fromLang"]);
            if (posts == 1) return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            Assert.AreEqual("fresh-token", form["token"]);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""[{"translations":[{"text":"translated"}]}]""")
            };
        });

        var result = await TranslateApi.GetTranslation("source", SourceTranslateLang.自动检测, TargetTranslateLang.English);

        Assert.AreEqual("translated", result);
        Assert.AreEqual(2, posts);
        Assert.AreEqual(2, authenticationRequests);
    }

    [TestMethod]
    [DataRow(HttpStatusCode.Unauthorized, 4)]
    [DataRow(HttpStatusCode.Forbidden, 4)]
    [DataRow(HttpStatusCode.InternalServerError, 2)]
    [DataRow(HttpStatusCode.TooManyRequests, 2)]
    public async Task Translate_RequestFails_ThrowsAfterBoundedAttempts(HttpStatusCode status, int expectedRequests)
    {
        var requests = 0;
        UseHandler((request, _) =>
        {
            requests++;
            return Task.FromResult(request.Method == HttpMethod.Get
                ? AuthenticationPage(request)
                : new HttpResponseMessage(status));
        });

        var error = await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            TranslateApi.GetTranslation("source", SourceTranslateLang.English, TargetTranslateLang.English));

        Assert.AreEqual(status, error.StatusCode);
        Assert.AreEqual(expectedRequests, requests);
    }

    [TestMethod]
    public async Task Translate_SuccessResponseContainsNoTranslation_ThrowsInsteadOfReturningErrorText()
    {
        UseHandler((request, _) => Task.FromResult(request.Method == HttpMethod.Get ? AuthenticationPage(request) : new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"error":"translations unavailable"}""")
        }));

        await Assert.ThrowsExactlyAsync<JsonException>(() =>
            TranslateApi.GetTranslation("source", SourceTranslateLang.English, TargetTranslateLang.English));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TranslateNode_Cancelled_PropagatesCancellationToHttpRequest(bool ocr)
    {
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        UseHandler(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Get) return AuthenticationPage(request);
            requested.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Cancelled requests must not complete.");
        });
        using var cancellation = new CancellationTokenSource();
        var node = new KitopiaEx.CustomScenarioMethods.Translate();
        Task translation = ocr
            ? node.TranslateOcrResults([new KitopiaEx.Ocr.OcrResult { Text = "source" }], SourceTranslateLang.English,
                TargetTranslateLang.English, cancellation.Token)
            : node.TranslateOcrResults("source", SourceTranslateLang.English, TargetTranslateLang.English, cancellation.Token);
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => translation.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private void UseHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    {
        _client = new HttpClient(new TestHandler(send));
        ClientField.SetValue(null, _client);
    }

    private static HttpResponseMessage AuthenticationPage(HttpRequestMessage request, string token = "fresh-token")
        => new(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(request.Method, "https://cn.bing.com/translator"),
            Content = new StringContent($$"""
                <div id="rich_tta" data-iid="translator.5023"></div>
                <script>var _G = {IG:"test-impression"}; var params_AbusePreventionHelper = [1791197251895,"{{token}}",3600000];</script>
                """)
        };

    [TestMethod]
    [DataRow(401)]
    [DataRow(403)]
    [DataRow(429)]
    public async Task Translate_ServiceErrorInsideSuccessfulHttpResponse_RefreshesOnlyAuthentication(int status)
    {
        var posts = 0;
        UseHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get) return Task.FromResult(AuthenticationPage(request));
            posts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"statusCode":{{status}},"errorMessage":"service error"}""")
            });
        });

        var exception = await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            TranslateApi.GetTranslation("hello", SourceTranslateLang.English, TargetTranslateLang.简体中文));

        Assert.AreEqual((HttpStatusCode)status, exception.StatusCode);
        Assert.AreEqual(status == 429 ? 1 : 2, posts);
    }

    [TestMethod]
    public async Task Translate_MultipleCalls_ReuseSessionAndEncodeText()
    {
        var pageRequests = 0;
        const string source = "line 1 & + = \"quoted\"\n中文";
        UseHandler(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                pageRequests++;
                return AuthenticationPage(request);
            }
            var form = System.Web.HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.AreEqual(source, form["text"]);
            Assert.AreEqual("en", form["fromLang"]);
            Assert.AreEqual("zh-Hant", form["to"]);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""[{"translations":[{"text":"translated"}]}]""")
            };
        });

        for (var index = 0; index < 2; index++)
            Assert.AreEqual("translated", await TranslateApi.GetTranslation(source, SourceTranslateLang.English,
                TargetTranslateLang.繁體中文));
        Assert.AreEqual(1, pageRequests);
    }

    [TestMethod]
    public async Task Translate_BlankText_DoesNotRequestAuthentication()
    {
        UseHandler((_, _) => throw new AssertFailedException("Blank text must not send an HTTP request."));
        Assert.AreEqual(" \n", await TranslateApi.GetTranslation(" \n", SourceTranslateLang.自动检测, TargetTranslateLang.English));
    }

    [TestMethod]
    public async Task Translate_AuthenticationPageInvalid_StopsBeforePosting()
    {
        UseHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>Authentication unavailable</html>")
        }));
        await Assert.ThrowsExactlyAsync<JsonException>(() =>
            TranslateApi.GetTranslation("hello", SourceTranslateLang.English, TargetTranslateLang.简体中文));
    }

    [TestMethod]
    public async Task OcrText_GraphConnection_FeedsTextTranslationWithoutChangingSourceResults()
    {
        UseHandler(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Get) return AuthenticationPage(request);
            var form = System.Web.HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.AreEqual("first\nsecond", form["text"]);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""[{"translations":[{"text":"translated"}]}]""")
            };
        });
        using var services = new ServiceCollection().AddSingleton<KitopiaEx.CustomScenarioMethods.Ocr>()
            .AddSingleton<KitopiaEx.CustomScenarioMethods.Translate>().BuildServiceProvider();
        var joinMethod = typeof(KitopiaEx.CustomScenarioMethods.Ocr)
            .GetMethod(nameof(KitopiaEx.CustomScenarioMethods.Ocr.JoinOcrText))!;
        var translationMethod = typeof(KitopiaEx.CustomScenarioMethods.Translate)
            .GetMethod(nameof(KitopiaEx.CustomScenarioMethods.Translate.TranslateOcrResults),
                [typeof(string), typeof(SourceTranslateLang), typeof(TargetTranslateLang), typeof(CancellationToken?)])!;
        var join = new ScenarioMethod(joinMethod, new PluginLocalInfo(), joinMethod.GetCustomAttribute<ScenarioMethodAttribute>()!,
            ScenarioMethodType.PluginMethod, services).GenerateNode();
        var translation = new ScenarioMethod(translationMethod, new PluginLocalInfo(),
            translationMethod.GetCustomAttribute<ScenarioMethodAttribute>()!, ScenarioMethodType.PluginMethod, services).GenerateNode();
        var source = new[] { new KitopiaEx.Ocr.OcrResult { Text = "first" }, new KitopiaEx.Ocr.OcrResult { Text = "second" } };
        join.Input[1].InputObject.Value = source;
        translation.Input[2].InputObject.Value = SourceTranslateLang.English;
        translation.Input[3].InputObject.Value = TargetTranslateLang.简体中文;
        Assert.IsTrue(ScenarioGraph.CanConnect(join.Output[1], translation.Input[1]));
        System.Collections.ObjectModel.ObservableCollection<ConnectionItem> connections =
            [new ConnectionItem { Source = join.Output[1], Target = translation.Input[1] }];
        var values = new ObservableDictionary<string, CustomScenarioValue>();

        Assert.IsTrue(await join.InvokeAsync(CancellationToken.None, connections, values, values, values));
        Assert.IsTrue(await translation.InvokeAsync(CancellationToken.None, connections, values, values, values));

        Assert.AreEqual("translated", translation.Output[1].InputObject.Value);
        Assert.AreEqual("first", source[0].Text);
        Assert.AreEqual("second", source[1].Text);
    }

    [TestMethod]
    [TestCategory("TranslationNetwork")]
    public async Task Translate_LiveMicrosoftService_ReturnsChineseText()
    {
        if (Environment.GetEnvironmentVariable("KITOPIA_TRANSLATION_LIVE_TEST") != "1")
            Assert.Inconclusive("Enable KITOPIA_TRANSLATION_LIVE_TEST to run the network smoke test.");
        var translated = await TranslateApi.GetTranslation("Hello world", SourceTranslateLang.English,
            TargetTranslateLang.简体中文);
        Assert.IsFalse(string.IsNullOrWhiteSpace(translated));
        Assert.AreNotEqual("Hello world", translated);
        Assert.IsTrue(translated.Any(character => character is >= '\u4e00' and <= '\u9fff'));
        Console.WriteLine(translated);
    }

    private sealed class TestHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }
}
