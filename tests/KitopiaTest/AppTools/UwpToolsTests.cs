using Kitopia.Desktop.Platform.Windows.AppTools;

namespace KitopiaTest.AppTools;

[TestClass]
public sealed class UwpToolsTests
{
    private string _temporaryDirectory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"Kitopia-UwpTools-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.Delete(_temporaryDirectory, true);
    }

    [TestMethod]
    public void ReadApplicationIconPaths_MultipleApplicationsAndQualifiedAssets_ReturnsMatchingIcons()
    {
        var assetsDirectory = Path.Combine(_temporaryDirectory, "Assets");
        Directory.CreateDirectory(assetsDirectory);
        var chatGptIcon = Path.Combine(assetsDirectory, "ChatGPT.targetsize-44_altform-unplated.png");
        var helperIcon = Path.Combine(assetsDirectory, "Helper.scale-200.png");
        File.WriteAllBytes(chatGptIcon, []);
        File.WriteAllBytes(helperIcon, []);
        File.WriteAllText(Path.Combine(_temporaryDirectory, "AppxManifest.xml"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                     xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10">
              <Applications>
                <Application Id="ChatGPT" Executable="app\ChatGPT.exe">
                  <uap:VisualElements DisplayName="ChatGPT" Square44x44Logo="Assets/ChatGPT.png" />
                </Application>
                <Application Id="Helper">
                  <uap:VisualElements DisplayName="Helper" Square44x44Logo="Assets\Helper.png" />
                </Application>
              </Applications>
            </Package>
            """);

        var icons = UwpTools.ReadApplicationIconPaths(_temporaryDirectory);

        Assert.HasCount(2, icons);
        Assert.AreEqual(chatGptIcon, icons["ChatGPT"]);
        Assert.AreEqual(helperIcon, icons["Helper"]);
    }

    [TestMethod]
    public void ResolveAssetPath_ExactAssetExists_PrefersExactAsset()
    {
        var assetsDirectory = Path.Combine(_temporaryDirectory, "Assets");
        Directory.CreateDirectory(assetsDirectory);
        var exactIcon = Path.Combine(assetsDirectory, "Logo.png");
        File.WriteAllBytes(exactIcon, []);
        File.WriteAllBytes(Path.Combine(assetsDirectory, "Logo.scale-200.png"), []);

        var result = UwpTools.ResolveAssetPath(_temporaryDirectory, "ms-appx:///Assets/Logo.png");

        Assert.AreEqual(exactIcon, result);
    }
}
