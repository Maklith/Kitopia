using System.Reflection;
using Kitopia.Desktop.Features.Services.Plugin;
using PluginCore;

namespace KitopiaTest.Services;

[TestClass]
public sealed class PluginOverallFeatureTests
{
    [TestMethod]
    public void AllFeatures_ContainsAllBuiltInFeatureCards()
    {
        var featureIds = PluginOverall.AllFeatures
            .Where(feature => feature.Source == "Kitopia")
            .Select(feature => feature.Id)
            .ToArray();

        string[] expectedFeatureIds =
        [
            "search",
            "index",
            "window-switcher",
            "window-topmost",
            "mouse-quick",
            "screen-capture",
            "file-locksmith",
            "lan-file-share",
            "device-chat",
            "scenario",
            "hotkey",
            "market",
            "plugin",
            "onnx",
            "settings"
        ];

        CollectionAssert.AreEquivalent(expectedFeatureIds, featureIds);
    }

    [TestMethod]
    public void BuiltInFeatures_AreRegisteredFromFeatureAttributes()
    {
        var declaredFeatures = typeof(KitopiaFeatures)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Select(method => method.GetCustomAttribute<FeatureAttribute>())
            .OfType<FeatureAttribute>()
            .ToDictionary(attribute => attribute.Id);
        var registeredFeatures = PluginOverall.AllFeatures
            .Where(feature => feature.Source == "Kitopia")
            .ToDictionary(feature => feature.Id);

        CollectionAssert.AreEquivalent(declaredFeatures.Keys, registeredFeatures.Keys);
        foreach (var (id, attribute) in declaredFeatures)
        {
            var feature = registeredFeatures[id];
            Assert.AreEqual(attribute.Name, feature.Name);
            Assert.AreEqual(attribute.Description, feature.Description);
            Assert.AreEqual(attribute.Category, feature.Category);
            Assert.AreEqual(attribute.IconSymbol, feature.IconSymbol);
            Assert.AreEqual(attribute.Order, feature.Order);
        }
    }
}
