using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace KitopiaTest.Architecture;

[TestClass]
public sealed class ProjectDependencyBoundaryTests
{
    private static readonly string[] MobileProjects =
    [
        "Mobile/Kitopia.Mobile/Kitopia.Mobile.csproj",
        "Mobile/Kitopia.Mobile.Android/Kitopia.Mobile.Android.csproj",
        "Mobile/Kitopia.Mobile.iOS/Kitopia.Mobile.iOS.csproj"
    ];

    private static readonly string[] SharedFeatureProjects =
    [
        "Kitopia.Feature/Kitopia.Feature.csproj",
        "Kitopia.Feature.Avalonia/Kitopia.Feature.Avalonia.csproj"
    ];

    private static readonly string[] DesktopHostProjects =
    [
        "Kitopia.Desktop.Abstractions/Kitopia.Desktop.Abstractions.csproj",
        "Kitopia.Desktop.Features/Kitopia.Desktop.Features.csproj",
        "Kitopia.Desktop.PluginSdk/Kitopia.Desktop.PluginSdk.csproj",
        "Kitopia.Desktop.Platform.Windows/Kitopia.Desktop.Platform.Windows.csproj",
        "Kitopia.Desktop.Platform.Linux/Kitopia.Desktop.Platform.Linux.csproj",
        "Kitopia.Desktop/Kitopia.Desktop.csproj"
    ];

    private static readonly string[] DesktopHostAssemblyNames =
    [
        "Kitopia.Desktop.Abstractions",
        "Kitopia.Desktop.Features",
        "PluginCore",
        "Kitopia.Desktop.Platform.Windows",
        "Kitopia.Desktop.Platform.Linux",
        "Kitopia.Desktop"
    ];

    private static readonly string[] MobileAssemblyNames =
    [
        "Kitopia.Mobile",
        "Kitopia.Mobile.Android",
        "Kitopia.Mobile.iOS"
    ];

    private static readonly HashSet<string> LinkedFileItemTypes = new(StringComparer.Ordinal)
    {
        "Compile",
        "AvaloniaResource",
        "AndroidResource",
        "Content",
        "None",
        "EmbeddedResource",
        "Resource"
    };

    private static readonly Regex DesktopLifetimePattern = new(
        @"\b(?:IClassicDesktopStyleApplicationLifetime|ClassicDesktopStyleApplicationLifetime|Avalonia\.Desktop)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    [TestMethod]
    public void ProjectReferences_RespectArchitectureBoundaries()
    {
        var repositoryRoot = FindRepositoryRoot();
        var graph = new ProjectReferenceGraph(repositoryRoot);
        var violations = new List<string>();

        AddForbiddenDependencyViolations(
            graph,
            violations,
            MobileProjects,
            DesktopHostProjects,
            "mobile projects must not depend on desktop host or plugin projects");

        AddForbiddenDependencyViolations(
            graph,
            violations,
            SharedFeatureProjects,
            [.. DesktopHostProjects, .. MobileProjects],
            "shared feature projects must remain independent of desktop and mobile hosts");

        AddForbiddenDependencyViolations(
            graph,
            violations,
            ["Kitopia.Desktop.PluginSdk/Kitopia.Desktop.PluginSdk.csproj"],
            [
                "Kitopia.Desktop.Abstractions/Kitopia.Desktop.Abstractions.csproj",
                "Kitopia.Desktop.Features/Kitopia.Desktop.Features.csproj",
                "Kitopia.Desktop.Platform.Windows/Kitopia.Desktop.Platform.Windows.csproj",
                "Kitopia.Desktop.Platform.Linux/Kitopia.Desktop.Platform.Linux.csproj",
                "Kitopia.Desktop/Kitopia.Desktop.csproj",
                .. MobileProjects
            ],
            "the plugin SDK must not depend on a host or platform implementation");

        AddForbiddenDependencyViolations(
            graph,
            violations,
            ["Kitopia.Desktop.Features/Kitopia.Desktop.Features.csproj"],
            [
                "Kitopia.Desktop.Platform.Windows/Kitopia.Desktop.Platform.Windows.csproj",
                "Kitopia.Desktop.Platform.Linux/Kitopia.Desktop.Platform.Linux.csproj",
                .. MobileProjects
            ],
            "the desktop feature project must not depend on a platform implementation or mobile host");

        AddForbiddenDependencyViolations(
            graph,
            violations,
            ["Kitopia.Desktop.Platform.Windows/Kitopia.Desktop.Platform.Windows.csproj"],
            ["Kitopia.Desktop.Platform.Linux/Kitopia.Desktop.Platform.Linux.csproj", .. MobileProjects],
            "the Windows adapter must not depend on Linux or mobile projects");

        AddForbiddenDependencyViolations(
            graph,
            violations,
            ["Kitopia.Desktop.Platform.Linux/Kitopia.Desktop.Platform.Linux.csproj"],
            ["Kitopia.Desktop.Platform.Windows/Kitopia.Desktop.Platform.Windows.csproj", .. MobileProjects],
            "the Linux adapter must not depend on Windows or mobile projects");

        AddForbiddenDependencyViolations(
            graph,
            violations,
            ["Kitopia.Desktop/Kitopia.Desktop.csproj"],
            MobileProjects,
            "the desktop composition root must not depend on a mobile host");

        AssertNoViolations(violations);
    }

    [TestMethod]
    public void DesktopFeaturesProject_ReferencesOnlyItsApprovedDependencies()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = ToAbsolutePath(
            repositoryRoot,
            "Kitopia.Desktop.Features/Kitopia.Desktop.Features.csproj");
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var document = XDocument.Load(projectPath);
        var references = document.Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, NormalizeSeparators(include!))))
            .Select(path => ToRepositoryPath(repositoryRoot, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                "Kitopia.Desktop.Abstractions/Kitopia.Desktop.Abstractions.csproj",
                "Kitopia.Desktop.PluginSdk/Kitopia.Desktop.PluginSdk.csproj",
                "Kitopia.Feature/Kitopia.Feature.csproj",
                "NodifyM.Avalonia/NodifyM.Avalonia/NodifyM.Avalonia.csproj",
                "PinyinM.NET/Pinyin.NET/Pinyin.NET.csproj"
            },
            references);
    }

    [TestMethod]
    public void RenamedProjects_UseCanonicalPathsAndPluginSdkCompatibilityMetadata()
    {
        var repositoryRoot = FindRepositoryRoot();
        var currentProjects = new[]
        {
            "Kitopia.Feature/Kitopia.Feature.csproj",
            "Kitopia.Feature.Avalonia/Kitopia.Feature.Avalonia.csproj",
            "Kitopia.Desktop.Abstractions/Kitopia.Desktop.Abstractions.csproj",
            "Kitopia.Desktop.Features/Kitopia.Desktop.Features.csproj",
            "Kitopia.Desktop/Kitopia.Desktop.csproj",
            "Kitopia.Desktop.Platform.Windows/Kitopia.Desktop.Platform.Windows.csproj",
            "Kitopia.Desktop.Platform.Linux/Kitopia.Desktop.Platform.Linux.csproj",
            "Kitopia.Desktop.PluginSdk/Kitopia.Desktop.PluginSdk.csproj"
        };
        var legacyProjects = new[]
        {
            "Kitopia.DeviceCommunication/Kitopia.DeviceCommunication.csproj",
            "Kitopia.DeviceCommunication.Avalonia/Kitopia.DeviceCommunication.Avalonia.csproj",
            "Kitopia.Feature.DeviceCommunication/Kitopia.Feature.DeviceCommunication.csproj",
            "Kitopia.Feature.DeviceCommunication.Avalonia/Kitopia.Feature.DeviceCommunication.Avalonia.csproj",
            "Kitopia.Desktop.Features.Search/Kitopia.Desktop.Features.Search.csproj",
            "Kitopia.Desktop.Features.CustomScenario/Kitopia.Desktop.Features.CustomScenario.csproj",
            "Kitopia.Desktop.Features.PluginHost/Kitopia.Desktop.Features.PluginHost.csproj",
            "Core/Core.csproj",
            "Kitopia.Desktop.Search/Kitopia.Desktop.Search.csproj",
            "Kitopia.Desktop.CustomScenario/Kitopia.Desktop.CustomScenario.csproj",
            "Kitopia.Desktop.PluginHost/Kitopia.Desktop.PluginHost.csproj",
            "KitopiaAvalonia/KitopiaAvalonia.csproj",
            "Core.Window/Core.Window.csproj",
            "Core.Linux/Core.Linux.csproj",
            "PluginCore/PluginCore.csproj"
        };

        foreach (var project in currentProjects)
        {
            Assert.IsTrue(File.Exists(ToAbsolutePath(repositoryRoot, project)),
                $"Renamed project is missing: {project}");
        }

        foreach (var project in legacyProjects)
        {
            Assert.IsFalse(File.Exists(ToAbsolutePath(repositoryRoot, project)),
                $"Legacy project path must not remain: {project}");
            Assert.IsFalse(Directory.Exists(Path.GetDirectoryName(ToAbsolutePath(repositoryRoot, project))!),
                $"Legacy project directory must not remain: {Path.GetDirectoryName(project)}");
        }

        var pluginSdkProject = XDocument.Load(ToAbsolutePath(
            repositoryRoot,
            "Kitopia.Desktop.PluginSdk/Kitopia.Desktop.PluginSdk.csproj"));
        Assert.AreEqual("PluginCore", GetProjectProperty(pluginSdkProject, "AssemblyName"));
        Assert.AreEqual("PluginCore", GetProjectProperty(pluginSdkProject, "RootNamespace"));

        var renamedProjectMetadata = new Dictionary<string, string>
        {
            ["Kitopia.Feature/Kitopia.Feature.csproj"] = "Kitopia.Feature",
            ["Kitopia.Feature.Avalonia/Kitopia.Feature.Avalonia.csproj"] = "Kitopia.Feature.Avalonia",
            ["Kitopia.Desktop.Abstractions/Kitopia.Desktop.Abstractions.csproj"] =
                "Kitopia.Desktop.Abstractions",
            ["Kitopia.Desktop.Features/Kitopia.Desktop.Features.csproj"] = "Kitopia.Desktop.Features",
            ["Kitopia.Desktop/Kitopia.Desktop.csproj"] = "Kitopia.Desktop"
        };
        foreach (var (project, expectedName) in renamedProjectMetadata)
        {
            var document = XDocument.Load(ToAbsolutePath(repositoryRoot, project));
            Assert.AreEqual(expectedName, GetProjectProperty(document, "AssemblyName"));
            Assert.AreEqual(expectedName, GetProjectProperty(document, "RootNamespace"));
        }
    }

    [TestMethod]
    public void PlatformSource_DoesNotDeclareLegacyNamespaces()
    {
        var repositoryRoot = FindRepositoryRoot();
        var platformProjects = new[]
        {
            "Kitopia.Desktop.Platform.Windows/Kitopia.Desktop.Platform.Windows.csproj",
            "Kitopia.Desktop.Platform.Linux/Kitopia.Desktop.Platform.Linux.csproj"
        };
        var legacyNamespacePattern = new Regex(
            @"\bCore\.(?:Window|Linux)\b",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
        var violations = platformProjects
            .Select(project => Path.GetDirectoryName(ToAbsolutePath(repositoryRoot, project))!)
            .SelectMany(EnumerateProjectSourceFiles)
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (Path: path, Line: line, Number: index + 1)))
            .Where(item => legacyNamespacePattern.IsMatch(item.Line))
            .Select(item => $"{ToRepositoryPath(repositoryRoot, item.Path)}:{item.Number} uses a legacy platform namespace")
            .ToArray();

        AssertNoViolations(violations);
    }

    [TestMethod]
    public void SharedAndMobileProjects_DoNotLinkDesktopFilesOrResources()
    {
        var repositoryRoot = FindRepositoryRoot();
        var protectedProjects = SharedFeatureProjects.Concat(MobileProjects);
        var forbiddenSourceRoots = DesktopHostProjects
            .Select(project => Path.GetDirectoryName(ToAbsolutePath(repositoryRoot, project))!)
            .ToArray();
        var violations = new List<string>();

        foreach (var project in protectedProjects)
        {
            var projectPath = ToAbsolutePath(repositoryRoot, project);
            var document = XDocument.Load(projectPath);
            var projectDirectory = Path.GetDirectoryName(projectPath)!;

            foreach (var linkedItem in document.Descendants()
                         .Where(element => LinkedFileItemTypes.Contains(element.Name.LocalName)))
            {
                var include = linkedItem.Attribute("Include")?.Value ?? linkedItem.Attribute("Update")?.Value;
                if (string.IsNullOrWhiteSpace(include) ||
                    include.Contains("$(", StringComparison.Ordinal) ||
                    include.IndexOfAny(['*', '?']) >= 0)
                {
                    continue;
                }

                var includedPath = Path.GetFullPath(Path.Combine(projectDirectory, NormalizeSeparators(include)));
                var forbiddenRoot = forbiddenSourceRoots.FirstOrDefault(root => IsWithin(includedPath, root));
                if (forbiddenRoot is null)
                {
                    continue;
                }

                violations.Add(
                    $"{ToRepositoryPath(repositoryRoot, projectPath)} links desktop {linkedItem.Name.LocalName} item " +
                    $"{ToRepositoryPath(repositoryRoot, includedPath)}");
            }
        }

        AssertNoViolations(violations);
    }

    [TestMethod]
    public void SharedAvaloniaSource_DoesNotUseDesktopLifetimeOrWindow()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = ToAbsolutePath(
            repositoryRoot,
            "Kitopia.Feature.Avalonia/Kitopia.Feature.Avalonia.csproj");
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var buildOutputDirectories = new[]
        {
            Path.Combine(projectDirectory, "bin"),
            Path.Combine(projectDirectory, "obj")
        };
        var forbiddenMarkers = new (string Description, Regex Pattern)[]
        {
            (
                "classic desktop application lifetime",
                new Regex(@"\bIClassicDesktopStyleApplicationLifetime\b", RegexOptions.CultureInvariant)),
            (
                "desktop application lifetime namespace",
                new Regex(@"\bAvalonia\.Controls\.ApplicationLifetimes\b", RegexOptions.CultureInvariant)),
            (
                "Avalonia Window type",
                new Regex(@"(?<![A-Za-z0-9_])(?:Avalonia\.Controls\.)?Window(?![A-Za-z0-9_])", RegexOptions.CultureInvariant))
        };
        var violations = new List<string>();

        foreach (var sourcePath in Directory.EnumerateFiles(projectDirectory, "*", SearchOption.AllDirectories)
                     .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                                    path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
                     .Where(path => buildOutputDirectories.All(directory => !IsWithin(path, directory))))
        {
            var lines = File.ReadAllLines(sourcePath);
            for (var index = 0; index < lines.Length; index++)
            {
                foreach (var marker in forbiddenMarkers)
                {
                    if (!marker.Pattern.IsMatch(lines[index]))
                    {
                        continue;
                    }

                    violations.Add(
                        $"{ToRepositoryPath(repositoryRoot, sourcePath)}:{index + 1} uses {marker.Description}");
                }
            }
        }

        AssertNoViolations(violations);
    }

    [TestMethod]
    public void SharedAndMobileProjects_DoNotReferenceForbiddenAssemblies()
    {
        var repositoryRoot = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (var project in SharedFeatureProjects.Concat(MobileProjects))
        {
            var projectPath = ToAbsolutePath(repositoryRoot, project);
            var document = XDocument.Load(projectPath);
            var forbiddenAssemblies = SharedFeatureProjects.Contains(project, StringComparer.OrdinalIgnoreCase)
                ? DesktopHostAssemblyNames.Concat(MobileAssemblyNames)
                : DesktopHostAssemblyNames;
            var forbiddenSet = forbiddenAssemblies.ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var reference in document.Descendants()
                         .Where(element => element.Name.LocalName == "Reference"))
            {
                var include = reference.Attribute("Include")?.Value;
                var assemblyName = include?.Split(',', 2)[0].Trim();
                if (!string.IsNullOrWhiteSpace(assemblyName) && forbiddenSet.Contains(assemblyName))
                {
                    violations.Add($"{project} references forbidden assembly {assemblyName}");
                }

                var hintPath = reference.Elements()
                    .FirstOrDefault(element => element.Name.LocalName == "HintPath")
                    ?.Value;
                if (string.IsNullOrWhiteSpace(hintPath) || hintPath.Contains("$(", StringComparison.Ordinal))
                {
                    continue;
                }

                var hintedAssemblyName = Path.GetFileNameWithoutExtension(hintPath);
                if (forbiddenSet.Contains(hintedAssemblyName))
                {
                    violations.Add($"{project} references forbidden assembly through HintPath {hintPath}");
                }
            }
        }

        AssertNoViolations(violations);
    }

    [TestMethod]
    public void SharedAndMobileSource_DoesNotUseHostNamespacesOrDesktopLifetime()
    {
        var repositoryRoot = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (var project in SharedFeatureProjects.Concat(MobileProjects))
        {
            var projectPath = ToAbsolutePath(repositoryRoot, project);
            var projectDirectory = Path.GetDirectoryName(projectPath)!;
            var forbiddenNamespaces = SharedFeatureProjects.Contains(project, StringComparer.OrdinalIgnoreCase)
                ? new[] { "Core", "PluginCore", "KitopiaAvalonia", "Kitopia.Mobile", "Kitopia.Desktop" }
                : new[] { "Core", "PluginCore", "KitopiaAvalonia", "Kitopia.Desktop" };

            foreach (var sourcePath in EnumerateProjectSourceFiles(projectDirectory))
            {
                var lines = File.ReadAllLines(sourcePath);
                for (var index = 0; index < lines.Length; index++)
                {
                    var line = lines[index];
                    var forbiddenNamespace = forbiddenNamespaces.FirstOrDefault(root => UsesNamespace(line, root));
                    if (forbiddenNamespace is not null)
                    {
                        violations.Add(
                            $"{ToRepositoryPath(repositoryRoot, sourcePath)}:{index + 1} uses forbidden namespace " +
                            forbiddenNamespace);
                    }

                    if (DesktopLifetimePattern.IsMatch(line))
                    {
                        violations.Add(
                            $"{ToRepositoryPath(repositoryRoot, sourcePath)}:{index + 1} uses desktop application lifetime");
                    }
                }
            }
        }

        AssertNoViolations(violations);
    }

    [TestMethod]
    public void SharedFeatureProjects_AreNotEmptyProjectShells()
    {
        var repositoryRoot = FindRepositoryRoot();
        var emptyProjects = SharedFeatureProjects.Append("Kitopia.Desktop.Features/Kitopia.Desktop.Features.csproj")
            .Where(project => !EnumerateProjectSourceFiles(
                Path.GetDirectoryName(ToAbsolutePath(repositoryRoot, project))!).Any())
            .ToArray();

        if (emptyProjects.Length > 0)
        {
            Assert.Fail("Shared feature projects must be created with their first feature slice, not as empty shells:" +
                        Environment.NewLine + string.Join(Environment.NewLine, emptyProjects));
        }
    }

    private static void AddForbiddenDependencyViolations(
        ProjectReferenceGraph graph,
        ICollection<string> violations,
        IEnumerable<string> owners,
        IEnumerable<string> forbiddenDependencies,
        string reason)
    {
        foreach (var owner in owners)
        {
            foreach (var forbiddenDependency in forbiddenDependencies)
            {
                var path = graph.FindPath(owner, forbiddenDependency);
                if (path is null)
                {
                    continue;
                }

                violations.Add($"{string.Join(" -> ", path)} ({reason})");
            }
        }
    }

    private static string? GetProjectProperty(XDocument document, string propertyName)
    {
        return document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == propertyName)
            ?.Value;
    }

    private static void AssertNoViolations(IReadOnlyCollection<string> violations)
    {
        if (violations.Count == 0)
        {
            return;
        }

        Assert.Fail("Project architecture boundary violations:" + Environment.NewLine +
                    string.Join(Environment.NewLine, violations.Select(violation => $"- {violation}")));
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

        throw new DirectoryNotFoundException(
            "Could not locate the repository root containing Kitopia.sln from the test process.");
    }

    private static string ToAbsolutePath(string repositoryRoot, string repositoryPath)
    {
        return Path.GetFullPath(Path.Combine(repositoryRoot, NormalizeSeparators(repositoryPath)));
    }

    private static string NormalizeSeparators(string path)
    {
        return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private static bool IsWithin(string path, string directory)
    {
        var relativePath = Path.GetRelativePath(directory, path);
        return relativePath != ".." &&
               !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !Path.IsPathRooted(relativePath);
    }

    private static IEnumerable<string> EnumerateProjectSourceFiles(string projectDirectory)
    {
        return Directory.EnumerateFiles(projectDirectory, "*", SearchOption.AllDirectories)
            .Where(path =>
            {
                var extension = Path.GetExtension(path);
                return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
                       extension.Equals(".axaml", StringComparison.OrdinalIgnoreCase);
            })
            .Where(path => !HasDirectorySegment(path, "bin") && !HasDirectorySegment(path, "obj"));
    }

    private static bool HasDirectorySegment(string path, string segment)
    {
        return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Contains(segment, StringComparer.OrdinalIgnoreCase);
    }

    private static bool UsesNamespace(string line, string namespaceRoot)
    {
        var escapedRoot = Regex.Escape(namespaceRoot);
        return Regex.IsMatch(
            line,
            $@"(?:^\s*(?:global\s+)?using\s+(?:(?:static\s+)|(?:[A-Za-z_][A-Za-z0-9_]*\s*=\s*))?(?:global::)?{escapedRoot}(?:\.|;))|" +
            $@"(?<![A-Za-z0-9_.])(?:global::)?{escapedRoot}\.|" +
            $@"clr-namespace:{escapedRoot}(?:\.|;)|assembly={escapedRoot}(?:[;\""']|$)",
            RegexOptions.CultureInvariant);
    }

    private static string ToRepositoryPath(string repositoryRoot, string path)
    {
        return Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');
    }

    private sealed class ProjectReferenceGraph(string repositoryRoot)
    {
        private readonly Dictionary<string, IReadOnlyList<string>> _references =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string>? FindPath(string owner, string forbiddenDependency)
        {
            var ownerPath = ToAbsolutePath(repositoryRoot, owner);
            var forbiddenPath = ToAbsolutePath(repositoryRoot, forbiddenDependency);
            var pending = new Queue<IReadOnlyList<string>>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ownerPath };
            pending.Enqueue([ownerPath]);

            while (pending.TryDequeue(out var currentPath))
            {
                var currentProject = currentPath[^1];
                foreach (var reference in GetReferences(currentProject))
                {
                    if (!visited.Add(reference))
                    {
                        continue;
                    }

                    var candidatePath = currentPath.Append(reference).ToArray();
                    if (string.Equals(reference, forbiddenPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidatePath
                            .Select(path => ToRepositoryPath(repositoryRoot, path))
                            .ToArray();
                    }

                    pending.Enqueue(candidatePath);
                }
            }

            return null;
        }

        private IReadOnlyList<string> GetReferences(string projectPath)
        {
            if (_references.TryGetValue(projectPath, out var references))
            {
                return references;
            }

            if (!File.Exists(projectPath))
            {
                throw new FileNotFoundException("An architecture boundary project does not exist.", projectPath);
            }

            var projectDirectory = Path.GetDirectoryName(projectPath)!;
            var document = XDocument.Load(projectPath);
            references = document.Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => include!)
                .Where(include => !include.Contains("$(", StringComparison.Ordinal))
                .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, NormalizeSeparators(include))))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _references.Add(projectPath, references);
            return references;
        }
    }
}
