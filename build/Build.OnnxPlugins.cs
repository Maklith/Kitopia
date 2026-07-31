using System.IO.Compression;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Serilog;

partial class Build
{
    Target PackOnnxRuntimeGpuWin => _ => _
        .DependsOn(CreateRelease, RestoreWindows)
        .OnlyWhenDynamic(() => Release is not null)
        .Executes(() => PublishStandaloneOnnxPlugin(
            RootDirectory / "OnnxRuntime.Gpu.Win" / "OnnxRuntime.Gpu.Win.csproj",
            "OnnxRuntime.Gpu.Win"));

    Target PackOnnxRuntimeOpenVino => _ => _
        .DependsOn(CreateRelease, RestoreWindows)
        .OnlyWhenDynamic(() => Release is not null)
        .Executes(() => PublishStandaloneOnnxPlugin(
            RootDirectory / "OnnxRuntime.OpenVino" / "OnnxRuntime.OpenVino.csproj",
            "OnnxRuntime.OpenVino"));

    Target PackOnnxPlugins => _ => _
        .DependsOn(PackOnnxRuntimeGpuWin, PackOnnxRuntimeOpenVino)
        .OnlyWhenDynamic(() => Release is not null);

    void PublishStandaloneOnnxPlugin(AbsolutePath project, string artifactName)
    {
        const string runtime = "win-x64";
        var output = ArtifactsDirectory / "plugins" / artifactName / runtime;
        output.DeleteDirectory();
        PublishPlugin(project, runtime, output);
        RemoveSymbolsAndDocs(output);

        var archive = RootDirectory /
                      $"Kitopia{AvaloniaProject.GetProperty("Version")}_{artifactName}_{runtime}.zip";
        archive.DeleteFile();
        output.ZipTo(archive, compressionLevel: CompressionLevel.SmallestSize);
        Log.Information("Created standalone ONNX plugin artifact {Artifact}", archive);
        UploadReleaseAsset(archive);
    }
}
