using System.Xml.Linq;

namespace KitopiaTest.Architecture;

[TestClass]
public sealed class AndroidRuntimeConfigurationTests
{
    [TestMethod]
    public void AndroidProject_UsesCoreClrForDebugAndNativeAotForRelease()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "Mobile",
            "Kitopia.Mobile.Android",
            "Kitopia.Mobile.Android.csproj");
        var project = XDocument.Load(projectPath);

        Assert.IsTrue(
            GetEffectiveProperty(project, "Release", "TargetFramework")
                ?.StartsWith("net10.0-android", StringComparison.OrdinalIgnoreCase) == true,
            "The Android application must target .NET 10 for Android.");

        Assert.AreEqual(false, GetEffectiveBoolean(project, "Debug", "UseMonoRuntime"));
        Assert.AreEqual(false, GetEffectiveBoolean(project, "Release", "UseMonoRuntime"));

        Assert.AreNotEqual(true, GetEffectiveBoolean(project, "Debug", "PublishAot"));
        Assert.AreEqual(true, GetEffectiveBoolean(project, "Release", "PublishAot"));

        Assert.AreNotEqual(true, GetEffectiveBoolean(project, "Debug", "PublishReadyToRun"));
        Assert.AreNotEqual(true, GetEffectiveBoolean(project, "Debug", "PublishReadyToRunComposite"));
        Assert.AreNotEqual(true, GetEffectiveBoolean(project, "Release", "PublishReadyToRun"));
        Assert.AreNotEqual(true, GetEffectiveBoolean(project, "Release", "PublishReadyToRunComposite"));
    }

    [TestMethod]
    public void AndroidPackaging_ExplicitlyPublishesNativeAot()
    {
        var repositoryRoot = FindRepositoryRoot();
        var buildScript = File.ReadAllText(Path.Combine(repositoryRoot, "build", "Build.Android.cs"));

        StringAssert.Contains(buildScript, ".SetProperty(\"PublishAot\", true)");
        Assert.IsFalse(
            buildScript.Contains("SetProperty(\"RunAOTCompilation\"", StringComparison.Ordinal),
            "Android packaging must not use the legacy Mono AOT switch.");
    }

    [TestMethod]
    public void AndroidNativeAot_PreservesCryptoTrustManagerJniEntryPoint()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "Mobile",
            "Kitopia.Mobile.Android",
            "Kitopia.Mobile.Android.csproj");
        var projectSource = File.ReadAllText(projectPath);
        const string jniSymbol =
            "Java_net_dot_android_crypto_DotnetProxyTrustManager_verifyRemoteCertificate";

        StringAssert.Contains(projectSource, $"--export-dynamic-symbol:{jniSymbol}");
        StringAssert.Contains(projectSource, $"--undefined={jniSymbol}");
    }

    private static bool? GetEffectiveBoolean(XDocument document, string configuration, string name)
    {
        var effectiveValue = GetEffectiveProperty(document, configuration, name);

        if (effectiveValue is null)
        {
            return null;
        }

        Assert.IsTrue(
            bool.TryParse(effectiveValue, out var result),
            $"{name} must be a literal boolean value, but was '{effectiveValue}'.");
        return result;
    }

    private static string? GetEffectiveProperty(XDocument document, string configuration, string name)
    {
        string? effectiveValue = null;

        foreach (var propertyGroup in document.Descendants()
                     .Where(element => element.Name.LocalName == "PropertyGroup"))
        {
            if (!AppliesToConfiguration(propertyGroup.Attribute("Condition")?.Value, configuration))
            {
                continue;
            }

            foreach (var property in propertyGroup.Elements()
                         .Where(element => element.Name.LocalName == name))
            {
                effectiveValue = property.Value.Trim();
            }
        }

        return effectiveValue;
    }

    private static bool AppliesToConfiguration(string? condition, string configuration)
    {
        if (string.IsNullOrWhiteSpace(condition) ||
            !condition.Contains("$(Configuration)", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedCondition = string.Concat(condition.Where(character => !char.IsWhiteSpace(character)))
            .Replace('\"', '\'');
        return normalizedCondition.Equals(
            $"'$(Configuration)'=='{configuration}'",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        foreach (var startPath in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(startPath);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Kitopia.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate Kitopia.sln.");
    }
}
