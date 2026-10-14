using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kitopia.Desktop.Features.CustomScenario;
using Kitopia.Desktop.Features.CustomScenario.Services;
using Kitopia.Desktop.Features.PluginHost.Services;
using Kitopia.Desktop.Features.Services.Config;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.CustomScenario;

namespace KitopiaTest.CustomScenario;

[TestClass]
[DoNotParallelize]
public sealed class ScenarioMarketTests
{
    [TestMethod]
    [DataRow(HttpStatusCode.Unauthorized, "重新登录")]
    [DataRow(HttpStatusCode.Forbidden, "重新授权")]
    public async Task SendAsync_AuthenticationRejected_PreservesStatusForAuthorizationPrompt(HttpStatusCode status, string action)
    {
        using var httpClient = new HttpClient(new ScenarioResponseHandler(status, ""));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://scenario.test/upload");

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            ScenarioMarketService.SendAsync<ScenarioMarketItem>(request, CancellationToken.None, httpClient));

        Assert.AreEqual(status, exception.StatusCode);
        StringAssert.Contains(exception.Message, action);
    }

    [TestMethod]
    public async Task SendAsync_UploadAccepted_ReturnsScenario()
    {
        using var httpClient = new HttpClient(new ScenarioResponseHandler(HttpStatusCode.OK,
            """{"flag":true,"data":{"id":42,"name":"Shared scenario","publicationStatus":0}}"""));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://scenario.test/upload");

        var scenario = await ScenarioMarketService.SendAsync<ScenarioMarketItem>(request, CancellationToken.None, httpClient);

        Assert.AreEqual(42L, scenario.Id);
        Assert.AreEqual("Shared scenario", scenario.Name);
    }

    [TestMethod]
    public async Task SendAsync_ValidationRejected_RemainsAnUploadError()
    {
        using var httpClient = new HttpClient(new ScenarioResponseHandler(HttpStatusCode.BadRequest,
            """{"flag":false,"data":"Invalid scenario JSON"}"""));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://scenario.test/upload");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ScenarioMarketService.SendAsync<ScenarioMarketItem>(request, CancellationToken.None, httpClient));

        Assert.AreEqual("Invalid scenario JSON", exception.Message);
    }

    [TestMethod]
    public void PrepareImport_SerializedScenario_PreservesGraphAndDisablesAutomaticActivation()
    {
        var previousServices = ServiceManager.Services;
        using var provider = new ServiceCollection().AddSingleton<IPluginManger, PluginMangerService>().BuildServiceProvider();
        ServiceManager.Services = provider;
        try
        {
            using var scenario = new Kitopia.Desktop.Features.CustomScenario.CustomScenario
            {
                Name = "Shared scenario", Description = "Description",
                RunHotKey = new HotKeyModel { IsEnabled = true },
                StopHotKey = new HotKeyModel { IsEnabled = true },
                LastRun = DateTime.Now
            };
            scenario.Nodes.Add(new ScenarioNodeBase { Title = "Start" });
            scenario.Nodes.Add(new ScenarioNodeBase { Title = "Tick" });
            scenario.Connections.Add(new ConnectionItem
            {
                Source = new ConnectorItem { Source = scenario.Nodes[0], InputObject = new CustomScenarioValue(typeof(int), 1) },
                Target = new ConnectorItem { Source = scenario.Nodes[1], InputObject = new CustomScenarioValue(typeof(int), 2) }
            });
            scenario.AutoTriggers.Add("Kitopia_SoftwareStarted");
            var root = JsonNode.Parse(JsonSerializer.Serialize(scenario, ConfigManger.DefaultOptions))!.AsObject();
            var nodes = root["Nodes"]!.ToJsonString();
            var uuid = ScenarioMarketService.PrepareImport(root);
            Assert.IsTrue(Guid.TryParse(uuid, out _));
            Assert.AreNotEqual(scenario.Uuid, uuid);
            Assert.AreEqual(nodes, root["Nodes"]!.ToJsonString());
            using var imported = JsonSerializer.Deserialize<Kitopia.Desktop.Features.CustomScenario.CustomScenario>(
                root.ToJsonString(), ConfigManger.DefaultOptions)!;
            Assert.AreEqual(uuid, imported.Uuid);
            Assert.AreEqual(scenario.Name, imported.Name);
            Assert.AreEqual(2, imported.Nodes.Count);
            Assert.AreSame(imported.Nodes[0], imported.Connections[0].Source.Source);
            Assert.AreSame(imported.Nodes[1], imported.Connections[0].Target.Source);
            Assert.IsEmpty(imported.AutoTriggers);
            Assert.IsNull(imported.RunHotKey);
            Assert.IsNull(imported.StopHotKey);
            Assert.IsFalse(imported.IsRunning);
            Assert.IsFalse(imported.IsActive);
            Assert.AreEqual(DateTime.MinValue, imported.LastRun);
        }
        finally
        {
            ServiceManager.Services = previousServices;
        }
    }

    [TestMethod]
    public void PrepareImport_SameScenarioTwice_CreatesDistinctLocalIdentifiers()
    {
        var root = JsonNode.Parse("""{"Name":"Scenario","Nodes":[],"Connections":[],"AutoTriggers":["Kitopia_SoftwareStarted"]}""")!.AsObject();
        var first = ScenarioMarketService.PrepareImport(root);
        var second = ScenarioMarketService.PrepareImport(root);
        Assert.AreNotEqual(first, second);
        Assert.AreEqual(0, root["AutoTriggers"]!.AsArray().Count);
    }

    [TestMethod]
    public void Parse_WebImportProtocol_ResolvesScenarioIdentifier()
    {
        var parsed = StartupArgumentManager.Parse(["kitopiaurl://action=DownloadScenario&value=42"]);
        Assert.AreEqual(StartupAction.DownloadScenario, parsed.Action);
        Assert.AreEqual("42", parsed.Value);
    }

    private sealed class ScenarioResponseHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
