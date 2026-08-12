using System.Collections.Generic;
using System.IO;
using Fallout.Common;
using Fallout.Common.CI.GitHubActions;
using Fallout.Common.IO;
using Fallout.Solutions;
using Fallout.Common.Tools.DotNet;
using Octokit;
using static Fallout.Common.Tools.DotNet.DotNetTasks;
using Project = Fallout.Solutions.Project;

[GitHubActions(
    "continuous",
    GitHubActionsImage.WindowsLatest,
    On = new[] { GitHubActionsTrigger.Push },
    ImportSecrets = new[] { nameof(Build.GitHubToken) },
    InvokedTargets = new[] { nameof(Build.Clean) },
    AutoGenerate = false)]
partial class Build : FalloutBuild
{
    internal const string ReleaseConfiguration = "Release";
    [Parameter("GitHub token used to create and upload a release")]
    [Secret]
    internal readonly string GitHubToken;

    [Solution]
    internal readonly Solution Solution;

    internal GitHubClient GitHubClient;
    internal Release Release;

    internal Project AvaloniaProject => Solution.GetProject("Kitopia.Desktop");
    internal AbsolutePath AndroidProjectFile => RootDirectory / "Mobile" / "Kitopia.Mobile.Android" /
                                                "Kitopia.Mobile.Android.csproj";
    internal bool IsRelease => !string.IsNullOrWhiteSpace(GitHubToken);
    internal AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

    Target Restore => _ => _
        .DependsOn(RestoreWindows, RestoreAndroid);

    Target Clean => _ => _
        .DependsOn(PackWindows, PackAndroid, PackInstaller)
        .Executes(() => { });

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

    public static int Main() => Execute<Build>(x => x.Clean);
}
