using System.Xml.Linq;

namespace KitopiaTest.Architecture;

[TestClass]
public sealed class ExplorerContextMenuRegistrationTests
{
    [TestMethod]
    [DataRow("src/Kitopia.StoreCompanionPacking/Package.appxmanifest")]
    [DataRow("src/Kitopia.StoreCompanionPacking/Package.debug.appxmanifest")]
    [DataRow("src/ContextMenuDll/SparsePackage.appxmanifest")]
    public void PackageManifest_RegistersModernAndClassicMenusForSameTargets(string manifestPath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Kitopia.sln")))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory, "Repository root not found.");
        var manifest = XDocument.Load(Path.Combine(directory.FullName, manifestPath));
        XNamespace foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        XNamespace desktop4 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/4";
        XNamespace desktop5 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/5";
        XNamespace desktop9 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/9";
        XNamespace com = "http://schemas.microsoft.com/appx/manifest/com/windows10";
        const string clsid = "60DA6757-67FE-B7CE-8195-3EFD30746B23";
        const string classicClsid = "CF88EA19-88F4-4C8A-9F8C-F593CE61D52D";
        string[] expectedTargets = ["*", "Directory", @"Directory\Background"];

        var extensions = manifest.Root!.Element(foundation + "Applications")!
            .Element(foundation + "Application")!.Element(foundation + "Extensions")!;
        var classicExtension = extensions.Elements(desktop9 + "Extension").Single();
        Assert.AreEqual("windows.fileExplorerClassicContextMenuHandler", (string?)classicExtension.Attribute("Category"));
        var handlers = classicExtension.Element(desktop9 + "FileExplorerClassicContextMenuHandler")!
            .Elements(desktop9 + "ExtensionHandler").ToArray();
        CollectionAssert.AreEquivalent(expectedTargets, handlers.Select(handler => (string)handler.Attribute("Type")!).ToArray());
        Assert.IsTrue(handlers.All(handler => (string?)handler.Attribute("Clsid") == classicClsid));

        var modernExtension = extensions.Elements(desktop4 + "Extension").Single();
        Assert.AreEqual("windows.fileExplorerContextMenus", (string?)modernExtension.Attribute("Category"));
        var modernTargets = modernExtension.Element(desktop4 + "FileExplorerContextMenus")!
            .Elements(desktop5 + "ItemType").ToArray();
        CollectionAssert.AreEquivalent(expectedTargets, modernTargets.Select(target => (string)target.Attribute("Type")!).ToArray());
        Assert.IsTrue(modernTargets.All(target => (string?)target.Element(desktop5 + "Verb")!.Attribute("Clsid") == clsid));
        var surrogateClasses = extensions.Descendants(com + "SurrogateServer").SelectMany(element => element.Elements(com + "Class")).ToArray();
        var surrogateClass = surrogateClasses.Single(element => (string?)element.Attribute("Id") == clsid);
        Assert.AreEqual(clsid, (string?)surrogateClass.Attribute("Id"));
        foreach (var handler in handlers)
        {
            var classicClass = surrogateClasses.Single(element => (string?)element.Attribute("Id") == (string?)handler.Attribute("Clsid"));
            Assert.AreEqual((string?)surrogateClass.Attribute("Path"), (string?)classicClass.Attribute("Path"),
                "Packaged classic menu handlers require a matching SurrogateServer/Class registration.");
            Assert.AreEqual("STA", (string?)classicClass.Attribute("ThreadingModel"));
        }
        Assert.IsTrue(((string)surrogateClass.Attribute("Path")!).EndsWith(
            manifestPath.Contains("debug", StringComparison.Ordinal) ? "ContextMenuDll.comhost.dll" : "ContextMenuDll.dll",
            StringComparison.Ordinal));

        var ignoredNamespaces = ((string)manifest.Root.Attribute("IgnorableNamespaces")!).Split(' ');
        CollectionAssert.Contains(ignoredNamespaces, "desktop9");
        var desktop = manifest.Descendants(foundation + "TargetDeviceFamily")
            .Single(family => (string?)family.Attribute("Name") == "Windows.Desktop");
        Assert.IsTrue(Version.Parse((string)desktop.Attribute("MaxVersionTested")!) >= new Version(10, 0, 21301, 0));
    }
}
