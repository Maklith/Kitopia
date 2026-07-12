using System;
using System.IO.Compression;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.Git;
using Serilog;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

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
        .Executes(() => PublishWindows("win-x64", includeNativeBackends: true));

    Target PackWindowsX86 => _ => _
        .DependsOn(CreateRelease, RestoreWindows)
        .Executes(() => PublishWindows("win-x86", includeNativeBackends: false));

    Target PackWindowsArm64 => _ => _
        .DependsOn(CreateRelease, RestoreWindows)
        .Executes(() => PublishWindows("win-arm64", includeNativeBackends: false));

    Target PackWindows => _ => _
        .DependsOn(PackWindowsX64, PackWindowsX86, PackWindowsArm64);

    // Kept as a compatibility alias for local scripts that used the old target name.
    Target CompileWindowsX64 => _ => _
        .DependsOn(PackWindowsX64);

    void PublishWindows(string runtime, bool includeNativeBackends)
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

        PublishPlugin(RootDirectory / "KitopiaEx" / "KitopiaEx.csproj", runtime,
            output / "plugins" / "kitopiaex");
        PublishPlugin(RootDirectory / "OnnxRuntime.CPU" / "OnnxRuntime.CPU.csproj", runtime,
            output / "plugins" / "kitopiaonnxruntimecpu");

        if (includeNativeBackends)
        {
            PublishPlugin(RootDirectory / "OnnxRuntime.Gpu.Win" / "OnnxRuntime.Gpu.Win.csproj", runtime,
                output / "plugins" / "kitopiaonnxruntimegpu");
            PublishPlugin(RootDirectory / "OnnxRuntime.OpenVino" / "OnnxRuntime.OpenVino.csproj", runtime,
                output / "plugins" / "kitopiaonnxruntimeopenvino");
        }

        RemoveSymbolsAndDocs(output);
        var archive = RootDirectory / $"Kitopia{AvaloniaProject.GetProperty("Version")}_{runtime}.zip";
        archive.DeleteFile();
        output.ZipTo(archive, compressionLevel: CompressionLevel.SmallestSize);
        Log.Information("Created Windows artifact {Artifact}", archive);
        UploadReleaseAsset(archive);
    }

    void PublishPlugin(AbsolutePath project, string runtime, AbsolutePath output)
    {
        var platformTarget = GetWindowsPlatformTarget(runtime);
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
    }

    static string GetWindowsPlatformTarget(string runtime) => runtime switch
    {
        "win-x86" => "x86",
        "win-x64" => "x64",
        "win-arm64" => "ARM64",
        _ => throw new ArgumentOutOfRangeException(nameof(runtime), runtime, "Unsupported Windows runtime")
    };
}
