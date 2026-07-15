using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace KitopiaTest.Architecture;

[TestClass]
public sealed class LayeringConventionTests
{
    private const string CrossPlatformFeatureProject =
        "Kitopia.Feature/Kitopia.Feature.csproj";

    private const string AvaloniaFeatureProject =
        "Kitopia.Feature.Avalonia/Kitopia.Feature.Avalonia.csproj";

    private const string DesktopAbstractionsProject =
        "Kitopia.Desktop.Abstractions/Kitopia.Desktop.Abstractions.csproj";

    private static readonly Regex PlatformBranchPattern = new(
        @"\b(?:(?:System\.)?OperatingSystem\.Is[A-Za-z0-9_]+|(?:System\.Runtime\.InteropServices\.)?RuntimeInformation\.IsOSPlatform)\s*\(",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PlatformUsingPattern = new(
        @"^\s*(?:global\s+)?using\s+(?:global::)?(?:Avalonia|Android|UIKit|Foundation|AppKit|Microsoft\.Win32|Windows)(?:[.;])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex NonAvaloniaPlatformUsingPattern = new(
        @"^\s*(?:global\s+)?using\s+(?:global::)?(?:Android|UIKit|Foundation|AppKit|Microsoft\.Win32|Windows)(?:[.;])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex QualifiedPlatformApiPattern = new(
        @"(?<![A-Za-z0-9_.])(?:global::)?(?:Android|UIKit|Foundation|AppKit|Microsoft\.Win32|Windows)\.",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex HostNamespacePattern = new(
        @"\bKitopia\.(?:Desktop|Mobile)(?:\.|\b)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PlatformCompilationPattern = new(
        @"^\s*#if[^\r\n]*(?:WINDOWS|LINUX|ANDROID|IOS|MACOS|TVOS|MACCATALYST)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Multiline);

    [TestMethod]
    public void CrossPlatformFeature_IsUiAndPlatformNeutral()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = ToAbsolutePath(repositoryRoot, CrossPlatformFeatureProject);
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var project = XDocument.Load(projectPath);
        var violations = new List<string>();

        foreach (var package in ProjectItems(project, "PackageReference"))
        {
            if (package.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase) ||
                package.Contains("Windows", StringComparison.OrdinalIgnoreCase) ||
                package.Contains("Android", StringComparison.OrdinalIgnoreCase) ||
                package.Contains("iOS", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"{CrossPlatformFeatureProject} references platform/UI package {package}");
            }
        }

        foreach (var targetFramework in ProjectItems(project, "TargetFramework", useElementValue: true))
        {
            if (IsPlatformTargetFramework(targetFramework))
            {
                violations.Add($"{CrossPlatformFeatureProject} targets platform-specific framework {targetFramework}");
            }
        }

        foreach (var reference in ResolveProjectReferences(project, projectDirectory))
        {
            var repositoryPath = ToRepositoryPath(repositoryRoot, reference);
            if (repositoryPath.Contains(".Avalonia/", StringComparison.OrdinalIgnoreCase) ||
                repositoryPath.StartsWith("Kitopia.Desktop", StringComparison.OrdinalIgnoreCase) ||
                repositoryPath.StartsWith("Mobile/", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"{CrossPlatformFeatureProject} references host/UI project {repositoryPath}");
            }
        }

        foreach (var sourcePath in EnumerateSources(projectDirectory))
        {
            var source = File.ReadAllText(sourcePath);
            if (PlatformUsingPattern.IsMatch(source))
            {
                violations.Add($"{ToRepositoryPath(repositoryRoot, sourcePath)} imports a UI/platform namespace");
            }

            if (QualifiedPlatformApiPattern.IsMatch(source))
            {
                violations.Add($"{ToRepositoryPath(repositoryRoot, sourcePath)} uses a platform-specific API");
            }

            if (PlatformBranchPattern.IsMatch(source))
            {
                violations.Add($"{ToRepositoryPath(repositoryRoot, sourcePath)} branches on the current OS");
            }

            if (PlatformCompilationPattern.IsMatch(source))
            {
                violations.Add($"{ToRepositoryPath(repositoryRoot, sourcePath)} uses a platform compilation branch");
            }
        }

        foreach (var markupPath in EnumerateMarkup(projectDirectory))
        {
            violations.Add($"{ToRepositoryPath(repositoryRoot, markupPath)} places Avalonia markup in a UI-neutral feature");
        }

        AssertNoViolations(violations);
    }

    [TestMethod]
    public void AvaloniaFeature_DependsOnItsFeatureButNotHosts()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = ToAbsolutePath(repositoryRoot, AvaloniaFeatureProject);
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var project = XDocument.Load(projectPath);
        var references = ResolveProjectReferences(project, projectDirectory)
            .Select(path => ToRepositoryPath(repositoryRoot, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        CollectionAssert.AreEqual(new[] { CrossPlatformFeatureProject }, references);

        var packages = ProjectItems(project, "PackageReference").ToArray();
        Assert.IsTrue(
            packages.Any(package => package.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase)),
            "The reusable Avalonia feature must own its UI dependency explicitly.");

        var violations = new List<string>();
        foreach (var targetFramework in ProjectItems(project, "TargetFramework", useElementValue: true))
        {
            if (IsPlatformTargetFramework(targetFramework))
            {
                violations.Add($"{AvaloniaFeatureProject} targets platform-specific framework {targetFramework}");
            }
        }

        foreach (var package in packages)
        {
            if (package.Contains("Android", StringComparison.OrdinalIgnoreCase) ||
                package.Contains("iOS", StringComparison.OrdinalIgnoreCase) ||
                package.Contains("Win32", StringComparison.OrdinalIgnoreCase) ||
                package.Contains("Windows", StringComparison.OrdinalIgnoreCase) ||
                package.Contains("MacCatalyst", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"{AvaloniaFeatureProject} references platform package {package}");
            }
        }

        foreach (var sourcePath in EnumerateSources(projectDirectory))
        {
            var source = File.ReadAllText(sourcePath);
            if (HostNamespacePattern.IsMatch(source))
            {
                violations.Add($"{ToRepositoryPath(repositoryRoot, sourcePath)} references an application host");
            }

            if (NonAvaloniaPlatformUsingPattern.IsMatch(source) ||
                PlatformBranchPattern.IsMatch(source) ||
                QualifiedPlatformApiPattern.IsMatch(source))
            {
                violations.Add($"{ToRepositoryPath(repositoryRoot, sourcePath)} uses a platform-specific API");
            }

            if (PlatformCompilationPattern.IsMatch(source))
            {
                violations.Add($"{ToRepositoryPath(repositoryRoot, sourcePath)} uses a platform compilation branch");
            }
        }

        foreach (var markupPath in EnumerateMarkup(projectDirectory))
        {
            var markup = File.ReadAllText(markupPath);
            if (HostNamespacePattern.IsMatch(markup) ||
                Regex.IsMatch(
                    markup,
                    @"using:(?:Android|UIKit|Foundation|AppKit|Microsoft\.Win32|Windows)(?:\.|\b)",
                    RegexOptions.CultureInvariant))
            {
                violations.Add($"{ToRepositoryPath(repositoryRoot, markupPath)} references a host/platform namespace");
            }
        }

        AssertNoViolations(violations);
    }

    [TestMethod]
    public void DesktopAbstractions_IsNonEmptyPureBclProject()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = ToAbsolutePath(repositoryRoot, DesktopAbstractionsProject);
        Assert.IsTrue(File.Exists(projectPath), $"Missing {DesktopAbstractionsProject}");

        var project = XDocument.Load(projectPath);
        Assert.AreEqual(0, ProjectItems(project, "PackageReference").Count());
        Assert.AreEqual(0, ProjectItems(project, "ProjectReference").Count());

        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var sources = EnumerateSources(projectDirectory).ToArray();
        Assert.IsTrue(sources.Length > 0, "Desktop.Abstractions must contain real contracts.");

        var violations = new List<string>();
        foreach (var sourcePath in sources)
        {
            var source = File.ReadAllText(sourcePath);
            if (Regex.IsMatch(
                    source,
                    @"^\s*using\s+(?:Avalonia|PluginCore|Kitopia\.Desktop\.Features|Kitopia\.Desktop\.Platform|Microsoft\.Win32|Android|UIKit|AppKit)",
                    RegexOptions.CultureInvariant | RegexOptions.Multiline))
            {
                violations.Add($"{ToRepositoryPath(repositoryRoot, sourcePath)} is not BCL-only");
            }
        }

        AssertNoViolations(violations);
    }

    private static IEnumerable<string> ProjectItems(
        XDocument project,
        string itemName,
        bool useElementValue = false)
    {
        return project.Descendants()
            .Where(element => element.Name.LocalName == itemName)
            .Select(element => useElementValue ? element.Value : element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim());
    }

    private static bool IsPlatformTargetFramework(string targetFramework)
    {
        return Regex.IsMatch(
            targetFramework,
            @"-(?:windows|android|ios|maccatalyst|tvos|macos)(?:[0-9.]|$)",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private static IEnumerable<string> ResolveProjectReferences(XDocument project, string projectDirectory)
    {
        return ProjectItems(project, "ProjectReference")
            .Where(include => !include.Contains("$(", StringComparison.Ordinal))
            .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, NormalizeSeparators(include))));
    }

    private static IEnumerable<string> EnumerateSources(string projectDirectory)
    {
        return Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !HasDirectorySegment(path, "bin") && !HasDirectorySegment(path, "obj"));
    }

    private static IEnumerable<string> EnumerateMarkup(string projectDirectory)
    {
        return Directory.EnumerateFiles(projectDirectory, "*.axaml", SearchOption.AllDirectories)
            .Where(path => !HasDirectorySegment(path, "bin") && !HasDirectorySegment(path, "obj"));
    }

    private static void AssertNoViolations(IReadOnlyCollection<string> violations)
    {
        if (violations.Count > 0)
        {
            Assert.Fail(string.Join(Environment.NewLine, violations.Select(violation => $"- {violation}")));
        }
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

    private static string ToAbsolutePath(string repositoryRoot, string repositoryPath)
    {
        return Path.GetFullPath(Path.Combine(repositoryRoot, NormalizeSeparators(repositoryPath)));
    }

    private static string NormalizeSeparators(string path)
    {
        return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private static bool HasDirectorySegment(string path, string segment)
    {
        return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Contains(segment, StringComparer.OrdinalIgnoreCase);
    }

    private static string ToRepositoryPath(string repositoryRoot, string path)
    {
        return Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');
    }
}
