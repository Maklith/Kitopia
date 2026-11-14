using System;
using System.Collections.Generic;
using System.IO.Compression;
using Fallout.Common;
using Fallout.Common.IO;
using Fallout.Solutions;
using Fallout.Common.Tools.DotNet;
using Fallout.Common.Tools.Git;
using Serilog;
using static Fallout.Common.Tools.DotNet.DotNetTasks;

partial class Build
{
    Target RestoreWindows => _ => _
        .Executes(() =>
        {
            GitTasks.Git("submodule update --init --recursive");
            DotNetRestore(c => c.SetProjectFile(AvaloniaProject.Path));
            foreach (var project in PluginProjects())
                DotNetRestore(c => c.SetProjectFile(project));
        });

    Target PackWindowsX64 => _ => _
        .DependsOn(CreateRelease, RestoreWindows)
        .OnlyWhenDynamic(() => Release is not null)
        .Executes(() => PublishWindows("win-x64"));

    Target PackWindowsArm64 => _ => _
        .DependsOn(CreateRelease, RestoreWindows)
        .OnlyWhenDynamic(() => Release is not null)
        .Executes(() => PublishWindows("win-arm64"));

    Target PackWindows => _ => _
        .DependsOn(PackWindowsX64, PackWindowsArm64)
        .OnlyWhenDynamic(() => Release is not null);

    // Kept as a compatibility alias for local scripts that used the old target name.
    Target CompileWindowsX64 => _ => _
        .DependsOn(PackWindowsX64)
        .OnlyWhenDynamic(() => Release is not null);

    void PublishWindows(string runtime)
    {
        var output = ArtifactsDirectory / "windows" / runtime;
        var platformTarget = GetWindowsPlatformTarget(runtime);
        output.DeleteDirectory();

        DotNetPublish(c => c
            .SetProject(AvaloniaProject.Path)
            .SetOutput(output)
            .SetRuntime(runtime)
            .SetFramework("net10.0-windows10.0.19041.0")
            .SetConfiguration(ReleaseConfiguration)
            .SetSelfContained(false)
            .SetProperty("Platform", platformTarget)
            .SetProperty("PlatformTarget", platformTarget));

        PublishPlugin(RootDirectory / "plugins" / "KitopiaEx" / "KitopiaEx.csproj", runtime,
            output / "plugins" / "kitopiaex");
        PublishPlugin(RootDirectory / "plugins" / "OnnxRuntime.CPU" / "OnnxRuntime.CPU.csproj", runtime,
            output / "plugins" / "kitopiaonnxruntimecpu");

        RemoveSymbolsAndDocs(output);
        var archive = RootDirectory / $"Kitopia{AvaloniaProject.GetProperty("Version")}_{runtime}.zip";
        archive.DeleteFile();
        output.ZipTo(archive, compressionLevel: CompressionLevel.SmallestSize);
        Log.Information("Created Windows artifact {Artifact}", archive);
        UploadReleaseAsset(archive);
    }

    void PublishPlugin(AbsolutePath project, string runtime, AbsolutePath output, IReadOnlyList<string> supportedPlatforms = null)
    {
        var platformTarget = GetPluginPlatformTarget(runtime);
        DotNetPublish(c => c
            .SetProject(project)
            .SetOutput(output)
            .SetRuntime(runtime)
            .SetFramework("net10.0")
            .SetConfiguration(ReleaseConfiguration)
            .SetSelfContained(false)
            .SetProperty("Platform", platformTarget)
            .SetProperty("PlatformTarget", platformTarget)
            .SetProperty("PublishTrimmed", false));
        StampPublishedManifestPlatforms(
            output / "manifest.json",
            supportedPlatforms ?? (string.IsNullOrWhiteSpace(runtime) ? null : [runtime]));
    }

    static string GetPluginPlatformTarget(string runtime) => runtime switch
    {
        null or "" => "AnyCPU",
        "win-x64" => "x64",
        "win-arm64" => "ARM64",
        "osx-x64" or "osx-arm64" or "linux-x64" or "linux-arm64" => "AnyCPU",
        _ => throw new ArgumentOutOfRangeException(nameof(runtime), runtime, "Unsupported plugin runtime")
    };

    static string GetWindowsPlatformTarget(string runtime) => runtime switch
    {
        "win-x64" => "x64",
        "win-arm64" => "ARM64",
        _ => throw new ArgumentOutOfRangeException(nameof(runtime), runtime, "Unsupported Windows runtime")
    };
}
