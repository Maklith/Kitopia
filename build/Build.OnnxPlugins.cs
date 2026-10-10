using System;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using Fallout.Common;
using Fallout.Common.IO;
using Fallout.Solutions;
using Serilog;

partial class Build
{
    Target PackOnnxRuntimeCpu => _ => _
        .DependsOn(RestoreWindows)
        .Executes(() => PublishOnnxRuntimeCpu());

    Target PackOnnxRuntimeGpuWin => _ => _
        .DependsOn(RestoreWindows)
        .Executes(() => PublishOnnxRuntimeGpu());

    Target PackOnnxRuntimeOpenVino => _ => _
        .DependsOn(RestoreWindows)
        .Executes(() => PublishOnnxRuntimeOpenVino());

    Target PackOnnxPlugins => _ => _
        .DependsOn(PackOnnxRuntimeCpu, PackOnnxRuntimeGpuWin, PackOnnxRuntimeOpenVino);

    void PublishOnnxPlugins()
    {
        PublishOnnxRuntimeCpu();
        PublishOnnxRuntimeGpu();
        PublishOnnxRuntimeOpenVino();
    }

    void PublishOnnxRuntimeCpu() => PublishStandaloneOnnxPlugin(
        Solution.AllProjects.Single(project => project.Name == "OnnxRuntime.CPU"),
        "kitopiaonnxruntimecpu",
        GetPluginApiKey("kitopiaonnxruntimecpu"));

    void PublishOnnxRuntimeGpu() => PublishStandaloneOnnxPlugin(
        Solution.AllProjects.Single(project => project.Name == "OnnxRuntime.Gpu.Win"),
        "kitopiaonnxruntimecuda",
        GetPluginApiKey("kitopiaonnxruntimecuda"));

    void PublishOnnxRuntimeOpenVino() => PublishStandaloneOnnxPlugin(
        Solution.AllProjects.Single(project => project.Name == "OnnxRuntime.OpenVino"),
        "kitopiaonnxruntimeopenvino",
        GetPluginApiKey("kitopiaonnxruntimeopenvino"));

    void PublishStandaloneOnnxPlugin(Project project, string nameSign, string apiKey)
    {
        const string runtime = "win-x64";
        var version = project.GetProperty("Version");
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidOperationException($"{project} must define its own Version.");
        ValidatePluginManifestVersion(project.Path, nameSign, version);
        if (!IsLocalPluginPublish && string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"No API key configured for {nameSign}.");
        var shouldUpload = true;
        try
        {
            if (!ShouldUploadPlugin(nameSign, version, apiKey)) return;
        }
        catch (Exception exception) when (IsLocalPluginPublish &&
            exception is HttpRequestException { StatusCode: null } or OperationCanceledException)
        {
            shouldUpload = false;
            Log.Warning("Skipping upload of {Plugin} {Version}: local plugin server {ApiUrl} is unavailable. {Error}",
                nameSign, version, PluginPublishApiUrl, exception.Message);
        }

        var output = ArtifactsDirectory / "plugins" / nameSign / runtime;
        output.DeleteDirectory();
        PublishPlugin(project.Path, runtime, output);
        RemoveSymbolsAndDocs(output);

        var archive = RootDirectory /
                      $"{nameSign}_{version}_{runtime}.zip";
        archive.DeleteFile();
        output.ZipTo(archive, compressionLevel: CompressionLevel.SmallestSize);
        if (shouldUpload)
            UploadPluginArchive(archive, nameSign, version, apiKey);
    }
}
