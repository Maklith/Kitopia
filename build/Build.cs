using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.Git;
using Nuke.Common.Tools.GitHub;
using Octokit;
using Serilog;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using Project = Nuke.Common.ProjectModel.Project;

[GitHubActions(
    "continuous",
    GitHubActionsImage.WindowsLatest,
    On = new[] { GitHubActionsTrigger.Push },
    ImportSecrets = new[] { nameof(GitHubToken) },
    InvokedTargets = new[] { nameof(Clean) })]
class Build : NukeBuild
{
    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Parameter] [Secret] readonly string GitHubToken;

    [Solution] readonly Solution Solution;

    Project AvaloniaProject => Solution.GetProject("KitopiaAvalonia");


    Target Restore => _ => _
        .Executes(() =>
        {
            Log.Debug("Restoring solution {0}", Solution);
            Log.Debug("Restoring project {0}", AvaloniaProject);
            GitTasks.Git("submodule update --init --recursive --remote");
            DotNetRestore(c => new DotNetRestoreSettings()
                .SetProjectFile(AvaloniaProject.Path)
                .SetRuntime("win-x64"));
            DotNetRestore(c => new DotNetRestoreSettings()
                .SetProjectFile("KitopiaEx")
                .SetRuntime("win-x64"));
        });

    Target CompileWindowsX64 => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(c => new DotNetBuildSettings()
                .SetProjectFile("KitopiaEx")
                .SetOutputDirectory(RootDirectory / "buildTest" / "plugins" / "KitopiaEx")
                .SetRuntime("win-x64"));
            DotNetBuild(c => new DotNetBuildSettings()
                .SetProjectFile(AvaloniaProject.Path)
                .SetOutputDirectory(RootDirectory / "buildTest")
                .SetRuntime("win-x64")
                .SetFramework("net8.0-windows10.0.17763.0")
                .SetConfiguration("Release")
            );
        });

    GitHubClient _gitHubClient;
    Release release;

    Target CreateRelease => _ => _.OnlyWhenDynamic(() =>
    {
        this._gitHubClient = new GitHubClient(new ProductHeaderValue("Kitopia"))
        {
            Credentials = new Credentials(GitHubToken)
        };
        var gitRepository = GitRepository.FromUrl("https://github.com/MakesYT/Kitopia");
        var result = GitHubTasks.GetLatestRelease(gitRepository, true)
            .Result;
        Log.Debug("Packing project {0}", AvaloniaProject);
        Log.Debug("GitHubName {0}", gitRepository.GetGitHubName());
        var _gitHubClient = new GitHubClient(new ProductHeaderValue("Kitopia"))
        {
            Credentials = new Credentials(GitHubToken)
        };

        var readOnlyList = _gitHubClient
            .Repository.GetAllTags(gitRepository.GetGitHubOwner(),
                gitRepository.GetGitHubName())
            .Result;
        if (readOnlyList.Any(e => e.Name == AvaloniaProject.GetProperty("Version")))
        {
            return false;
        }

        return true;
    }).DependsOn(CompileWindowsX64).Executes(() =>
    {
        StringBuilder body = new StringBuilder();
        var gitRepository = GitRepository.FromUrl("https://github.com/MakesYT/Kitopia");
        var repositoryTags = _gitHubClient
            .Repository.GetAllTags(gitRepository.GetGitHubOwner(),
                gitRepository.GetGitHubName())
            .Result;
        if (repositoryTags.Count <= 0)
        {
            body.AppendLine("无明确更新说明");
        }
        else
        {
            string lastCommit = GitTasks.GitCurrentCommit();
            Log.Debug("Last commit {0}", lastCommit);
            var repositoryTag = repositoryTags.First();
            Log.Debug("First commit {0}", repositoryTag.Commit.Sha);
            while (lastCommit != repositoryTag.Commit.Sha)
            {
                var gitHubCommit = _gitHubClient
                    .Repository.Commit.Get(gitRepository.GetGitHubOwner(),
                        gitRepository.GetGitHubName(), lastCommit)
                    .Result;
                if (gitHubCommit.Commit.Message.Length >= 3)
                {
                    if (!gitHubCommit.Commit.Message.StartsWith("*"))
                    {
                        body.AppendLine(gitHubCommit.Commit.Message);
                    }
                }

                lastCommit = gitHubCommit.Parents.First()
                    .Sha;
                Log.Debug(lastCommit);
            }
        }

        var tag = _gitHubClient.Git.Tag.Create(gitRepository.GetGitHubOwner(),
                gitRepository.GetGitHubName(),
                new NewTag()
                {
                    Object = GitTasks.GitCurrentCommit(),
                    Tag = AvaloniaProject.GetProperty("Version"),
                    Message = AvaloniaProject.GetProperty("Version"),
                })
            .Result;
        var reference = _gitHubClient.Git.Reference.Create(gitRepository.GetGitHubOwner(),
                gitRepository.GetGitHubName(),
                new NewReference(
                    "refs/tags/" +
                    AvaloniaProject.GetProperty("Version"),
                    GitTasks.GitCurrentCommit()))
            .Result;
        var newRelease = new NewRelease(AvaloniaProject.GetProperty("Version"))
        {
            Name = AvaloniaProject.GetProperty("Version"),
            Prerelease = true,
            Draft = false,
            Body = body.ToString()
        };
        release = _gitHubClient.Repository.Release.Create(
                gitRepository.GetGitHubOwner(),
                gitRepository.GetGitHubName(),
                newRelease)
            .Result;
    });

    Target PackDebug => _ => _
        .OnlyWhenDynamic(() => FinishedTargets.Contains(CreateRelease))
        .DependsOn(CreateRelease)
        .Executes(() =>
        {
            var rootDirectory = RootDirectory / "buildTest";
            var archiveFile = RootDirectory / "Kitopia" + AvaloniaProject.GetProperty("Version") +
                              "_Debug_WithoutContained.zip";
            archiveFile.DeleteFile();
            rootDirectory.ZipTo(archiveFile);
            Log.Debug("Uploading artifact {0}", archiveFile);
            using var artifactStream = File.OpenRead(archiveFile);
            var assetUpload = new ReleaseAssetUpload
            {
                FileName = archiveFile.Name,
                ContentType = "application/octet-stream",
                RawData = artifactStream,
            };
            _gitHubClient.Repository.Release.UploadAsset(release, assetUpload)
                .Wait();
        });

    Target Pack => _ => _
        .OnlyWhenDynamic(() => FinishedTargets.Contains(CreateRelease))
        .DependsOn(CreateRelease)
        .Executes(() =>
            {
                var rootDirectory = RootDirectory / "Publish";
                rootDirectory.DeleteDirectory();
                DotNetPublish(c => new DotNetPublishSettings()
                    .SetProject("KitopiaEx")
                    .SetOutput(RootDirectory / "Publish" / "plugins" / "KitopiaEx")
                    .SetRuntime("win-x64")
                    .SetFramework("net8.0")
                    .SetConfiguration("Release")
                    .SetSelfContained(false)
                );
                DotNetPublish(c => new DotNetPublishSettings()
                    .SetProject(AvaloniaProject.Name)
                    .SetOutput(RootDirectory / "Publish")
                    .SetPublishSingleFile(true)
                    .SetRuntime("win-x64")
                    .SetFramework("net8.0-windows10.0.17763.0")
                    .SetConfiguration("Release")
                    .SetSelfContained(false)
                );
                foreach (var absolutePath in rootDirectory.GetFiles())
                {
                    if (absolutePath.Extension is ".pdb" or ".xml")
                    {
                        absolutePath.DeleteFile();
                    }
                }

                var archiveFile = RootDirectory / "Kitopia" + AvaloniaProject.GetProperty("Version") +
                                  "_WithoutContained.zip";
                archiveFile.DeleteFile();
                rootDirectory.ZipTo(archiveFile);
                Log.Debug("Uploading artifact {0}", archiveFile);
                using var artifactStream = File.OpenRead(archiveFile);
                var assetUpload = new ReleaseAssetUpload
                {
                    FileName = archiveFile.Name,
                    ContentType = "application/octet-stream",
                    RawData = artifactStream,
                };
                _gitHubClient.Repository.Release.UploadAsset(release, assetUpload)
                    .Wait();
            }
        );


    Target PackSelf => _ => _
        .OnlyWhenDynamic(() => FinishedTargets.Contains(CreateRelease))
        .DependsOn(CreateRelease)
        .Executes(() =>
            {
                var rootDirectory_self = RootDirectory / "Publish_SelfContained";
                rootDirectory_self.DeleteDirectory();
                DotNetPublish(c => new DotNetPublishSettings()
                    .SetProject("KitopiaEx")
                    .SetOutput(RootDirectory / "Publish_SelfContained" / "plugins" /
                               "KitopiaEx")
                    .SetRuntime("win-x64")
                    .SetFramework("net8.0")
                    .SetConfiguration("Release")
                    .SetSelfContained(true)
                );
                DotNetPublish(c => new DotNetPublishSettings()
                    .SetProject(AvaloniaProject.Name)
                    .SetOutput(RootDirectory / "Publish_SelfContained")
                    .SetPublishSingleFile(true)
                    .SetRuntime("win-x64")
                    .SetFramework("net8.0-windows10.0.17763.0")
                    .SetConfiguration("Release")
                    .SetSelfContained(true)
                );
                var archiveFile_self = RootDirectory / "Kitopia" +
                                       AvaloniaProject.GetProperty("Version") + "_SelfContained.zip";
                archiveFile_self.DeleteFile();
                foreach (var absolutePath in rootDirectory_self.GetFiles())
                {
                    if (absolutePath.Extension is ".pdb" or ".xml")
                    {
                        absolutePath.DeleteFile();
                    }
                }

                rootDirectory_self.ZipTo(archiveFile_self, compressionLevel: CompressionLevel.SmallestSize);
                var assetUpload_self = new ReleaseAssetUpload
                {
                    FileName = archiveFile_self.Name,
                    ContentType = "application/octet-stream",
                    RawData = File.OpenRead(archiveFile_self),
                };
                _gitHubClient.Repository.Release.UploadAsset(release, assetUpload_self)
                    .Wait();
            }
        );

    Target Clean => _ => _
        .DependsOn(PackDebug)
        .DependsOn(Pack)
        .DependsOn(PackSelf)
        .Executes(() =>
        {
        });

    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode
    public static int Main() => Execute<Build>(x => x.CompileWindowsX64);
}