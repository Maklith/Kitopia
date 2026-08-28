using System.Text.Json;
using Kitopia.Desktop.Features.Services.Plugin;
using Newtonsoft.Json;

namespace KitopiaTest.Services;

[TestClass]
public sealed class PluginContractTests
{
    [TestMethod]
    public void PluginListResponse_DeserializesPagedV1Contract()
    {
        const string json = """
            {
              "flag": true,
              "data": {
                "items": [
                  {
                    "id": 42,
                    "authorId": 7,
                    "name": "Example",
                    "nameSign": "example",
                    "lastVersion": "1.2.0",
                    "supportSystems": ["windows"],
                    "downloadCounts": 10,
                    "rank": 1,
                    "descriptionShort": "Short description"
                  }
                ],
                "page": 1,
                "pageSize": 20,
                "totalCount": 21,
                "totalPages": 2
              }
            }
            """;

        var response = JsonConvert.DeserializeObject<PluginApiResponse<PluginPage>>(json);

        Assert.IsNotNull(response);
        Assert.IsTrue(response.Flag);
        Assert.IsNotNull(response.Data);
        Assert.AreEqual(2, response.Data.TotalPages);
        Assert.AreEqual("example", response.Data.Items[0].NameSign);
        Assert.AreEqual("1.2.0", response.Data.Items[0].LastVersion);
    }

    [TestMethod]
    public void PluginManifest_ConvertsOnlyRuntimeFields()
    {
        const string json = """
            {
              "Id": 42,
              "AuthorId": 7,
              "IsPublic": true,
              "VersionId": 3,
              "Name": "Example",
              "NameSign": "example",
              "Version": "1.2.0",
              "Description": "Example plugin",
              "Main": "Example.dll",
              "Dependencies": { "Kitopia": "^1.0.0" }
            }
            """;

        var manifest = System.Text.Json.JsonSerializer.Deserialize<PluginManifest>(json);
        var plugin = manifest!.ToPluginBaseInfo();

        Assert.AreEqual("Example", plugin.Name);
        Assert.AreEqual("example", plugin.NameSign);
        Assert.AreEqual("1.2.0", plugin.Version);
        Assert.AreEqual("Example.dll", plugin.Main);
        Assert.AreEqual("^1.0.0", plugin.Dependencies["Kitopia"]);
        Assert.AreEqual(0, plugin.Id);
        Assert.AreEqual(0, plugin.VersionId);
    }

    [TestMethod]
    public void IsVersionNewer_UsesSemanticVersionPrecedence()
    {
        Assert.IsTrue(PluginDependencyService.IsVersionNewer("1.0.0", "1.0.0-rc.1"));
        Assert.IsTrue(PluginDependencyService.IsVersionNewer("1.0.0-rc.2", "1.0.0-rc.1"));
        Assert.IsFalse(PluginDependencyService.IsVersionNewer("1.0.0-rc.1", "1.0.0"));
        Assert.IsFalse(PluginDependencyService.IsVersionNewer("1.0.0+build.2", "1.0.0+build.1"));
    }
}
