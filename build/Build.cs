using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Fallout.Common;
using Fallout.Common.CI.GitHubActions;
using Fallout.Common.IO;
using Fallout.Solutions;
using Fallout.Common.Tools.DotNet;
using NuGet.Versioning;
using Octokit;
using Serilog;
using static Fallout.Common.Tools.DotNet.DotNetTasks;
using Project = Fallout.Solutions.Project;

[GitHubActions(
    "continuous",
    GitHubActionsImage.WindowsLatest,
    On = new[] { GitHubActionsTrigger.Push },
    ImportSecrets = new[]
    {
        nameof(Build.GitHubToken),
        nameof(Build.PluginApiKeyCpu),
        nameof(Build.PluginApiKeyGpu),
        nameof(Build.PluginApiKeyOpenVino)
    },
    InvokedTargets = new[] { nameof(Build.Clean) },
    AutoGenerate = false)]
partial class Build : FalloutBuild
{
    internal const string ReleaseConfiguration = "Release";
    [Parameter("GitHub token used to create and upload a release")]
    [Secret]
    internal readonly string GitHubToken;

    [Parameter("API base URL used to publish ONNX plugins (defaults to localhost)")]
    internal readonly string PluginApiUrl;

    [Parameter("Fallback API key for local plugin publishing")]
    [Secret]
    internal readonly string PluginApiKey;

    [Parameter("API key for the ONNX CPU plugin")]
    [Secret]
    internal readonly string PluginApiKeyCpu;

    [Parameter("API key for the ONNX CUDA plugin")]
    [Secret]
    internal readonly string PluginApiKeyGpu;

    [Parameter("API key for the ONNX OpenVINO plugin")]
    [Secret]
    internal readonly string PluginApiKeyOpenVino;

    [Solution]
    internal readonly Solution Solution;

    internal GitHubClient GitHubClient;
    internal Release Release;

    internal Project AvaloniaProject => Solution.GetProject("Kitopia.Desktop");
    internal AbsolutePath AndroidProjectFile => RootDirectory / "Mobile" / "Kitopia.Mobile.Android" /
                                                "Kitopia.Mobile.Android.csproj";
    internal bool IsRelease => !string.IsNullOrWhiteSpace(GitHubToken);
    internal AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    internal string PluginPublishApiUrl =>
        (string.IsNullOrWhiteSpace(PluginApiUrl)
            ? IsRelease ? "https://api.kitopia.top:5111" : "https://localhost:5111"
            : PluginApiUrl).TrimEnd('/');

    Target Restore => _ => _
        .DependsOn(RestoreWindows, RestoreAndroid);

    Target Clean => _ => _
        .DependsOn(PackWindows, PackOnnxPlugins, PackAndroid, PackInstaller)
        .Executes(() => { });

    Target LocalTest => _ => _
        .DependsOn(RestoreWindows)
        .Executes(async () =>
        {
            await PublishWindowsAsync("win-x64");
            PublishOnnxPlugins();
            BuildInstaller("win-x64", "x86_64-pc-windows-msvc");
        });

    internal IEnumerable<AbsolutePath> PluginProjects()
    {
        yield return RootDirectory / "KitopiaEx" / "KitopiaEx.csproj";
        yield return RootDirectory / "OnnxRuntime.CPU" / "OnnxRuntime.CPU.csproj";
        yield return RootDirectory / "OnnxRuntime.Gpu.Win" / "OnnxRuntime.Gpu.Win.csproj";
        yield return RootDirectory / "OnnxRuntime.OpenVino" / "OnnxRuntime.OpenVino.csproj";
    }

    internal void RemoveSymbolsAndDocs(AbsolutePath directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            if (Path.GetExtension(file) is ".pdb" or ".xml")
                File.Delete(file);
    }

    internal void UploadReleaseAsset(AbsolutePath archiveFile)
    {
        if (!IsRelease || Release is null)
            return;

        using var artifactStream = File.OpenRead(archiveFile);
        GitHubClient.Repository.Release.UploadAsset(Release, new ReleaseAssetUpload
        {
            FileName = archiveFile.Name,
            ContentType = "application/octet-stream",
            RawData = artifactStream
        }).Wait();
    }

    internal string GetPluginApiKey(string nameSign) => nameSign switch
    {
        "kitopiaonnxruntimecpu" => PluginApiKeyCpu ?? PluginApiKey,
        "kitopiaonnxruntimecuda" => PluginApiKeyGpu ?? PluginApiKey,
        "kitopiaonnxruntimeopenvino" => PluginApiKeyOpenVino ?? PluginApiKey,
        _ => PluginApiKey
    };

    internal bool ShouldUploadPlugin(string nameSign, string version, string apiKey)
    {
        using var client = CreatePluginHttpClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{PluginPublishApiUrl}/api/v1/plugin/{Uri.EscapeDataString(nameSign)}");
        request.Headers.Add("X-API-Key", apiKey);
        using var response = client.Send(request);
        var responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException($"Plugin {nameSign} is not registered on {PluginPublishApiUrl}.");
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(responseBody);
        if (json.RootElement.TryGetProperty("flag", out var responseFlag) && !responseFlag.GetBoolean())
            throw new InvalidOperationException($"Web server rejected version check for {nameSign}: {responseBody}");
        if (!json.RootElement.TryGetProperty("data", out var data))
            return true;
        var latest = data.TryGetProperty("latestReleaseVersion", out var managedVersion) &&
                     managedVersion.ValueKind is not JsonValueKind.Null &&
                     !string.IsNullOrWhiteSpace(managedVersion.GetString())
            ? managedVersion
            : data.TryGetProperty("lastVersion", out var publicVersion) ? publicVersion : default;
        if (latest.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null || string.IsNullOrWhiteSpace(latest.GetString()))
            return true;
        if (!NuGetVersion.TryParse(version, out var localVersion) ||
            !NuGetVersion.TryParse(latest.GetString(), out var remoteVersion))
            throw new InvalidOperationException($"Invalid version for {nameSign}: local {version}, remote {latest.GetString()}.");
        var comparison = VersionComparer.VersionRelease.Compare(localVersion, remoteVersion);
        if (comparison < 0)
            throw new InvalidOperationException(
                $"Plugin {nameSign} project version {version} is older than the published version {remoteVersion}.");
        if (comparison == 0)
        {
            Log.Information("Skipping {Plugin}: version {Version} is already published", nameSign, version);
            return false;
        }
        return true;
    }

    internal void ValidatePluginManifestVersion(AbsolutePath projectPath, string nameSign, string version)
    {
        var manifestPath = Path.Combine(Path.GetDirectoryName(projectPath.ToString())!, "manifest.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var manifestName = manifest.RootElement.GetProperty("NameSign").GetString();
        var manifestVersion = manifest.RootElement.GetProperty("Version").GetString();
        if (!string.Equals(manifestName, nameSign, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(manifestVersion, version, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{manifestPath} must declare NameSign={nameSign} and Version={version}.");
        }
    }

    private HttpClient CreatePluginHttpClient()
    {
        var handler = new HttpClientHandler();
        if (Uri.TryCreate(PluginPublishApiUrl, UriKind.Absolute, out var apiUri) && apiUri.IsLoopback)
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        return new HttpClient(handler);
    }

    internal void UploadPluginArchive(AbsolutePath archiveFile, string nameSign, string version, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                $"PluginApiKey is required to publish {nameSign}. Generate an API key for this plugin first.");

        using var client = CreatePluginHttpClient();
        client.Timeout = TimeSpan.FromMinutes(15);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{PluginPublishApiUrl}/api/v1/plugin/uploadarchive/1/{Uri.EscapeDataString(nameSign)}/{Uri.EscapeDataString(version)}");
        request.Headers.Add("X-API-Key", apiKey);
        request.Headers.ExpectContinue = true;
        using var content = new MultipartFormDataContent();
        using var stream = File.OpenRead(archiveFile);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(fileContent, "file", archiveFile.Name);
        request.Content = content;

        Log.Information("Uploading {Plugin} {Version}: {SizeMiB:F2} MiB to {ApiUrl}",
            nameSign, version, stream.Length / (1024d * 1024), PluginPublishApiUrl);
        try
        {
            using var response = client.SendAsync(request).GetAwaiter().GetResult();
            var responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Failed to publish {nameSign} {version}: {(int)response.StatusCode} {response.ReasonPhrase}. {responseBody}");

            using var json = JsonDocument.Parse(responseBody);
            if (json.RootElement.TryGetProperty("flag", out var flag) && !flag.GetBoolean())
                throw new InvalidOperationException($"Web server rejected {nameSign} {version}: {responseBody}");
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                $"Upload of {nameSign} {version} ({stream.Length / (1024d * 1024):F2} MiB) to {PluginPublishApiUrl} failed. " +
                "Check the server logs and request body size limits on the server and any reverse proxy.", exception);
        }
        catch (OperationCanceledException exception)
        {
            throw new InvalidOperationException(
                $"Upload of {nameSign} {version} to {PluginPublishApiUrl} timed out after 15 minutes.", exception);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Web server returned invalid JSON for {nameSign} {version}.", exception);
        }
        Log.Information("Published plugin {Plugin} {Version} to {ApiUrl}", nameSign, version, PluginPublishApiUrl);
    }

    public static int Main() => Execute<Build>(x => x.IsRelease ? x.Clean : x.LocalTest);
}
