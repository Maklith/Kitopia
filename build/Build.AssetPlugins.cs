using System.Linq;
using System.Threading.Tasks;
using Fallout.Common;

partial class Build
{
    Target PackFfmpegPlugin => _ => _
        .DependsOn(RestoreWindows)
        .Executes(() => PublishFfmpegPlugin());

    Target PackSemanticSearchPlugin => _ => _
        .DependsOn(RestoreWindows)
        .Executes(() => PublishSemanticSearchPlugin());

    Target PackAssetPlugins => _ => _
        .DependsOn(PackFfmpegPlugin, PackSemanticSearchPlugin);

    async Task PublishAssetPlugins()
    {
        await PublishFfmpegPlugin();
        await PublishSemanticSearchPlugin();
    }

    async Task PublishFfmpegPlugin()
    {
        var project = Solution.AllProjects.Single(project => project.Name == "KitopiaFfmpeg");
        var apiKey = GetPluginApiKey("kitopiaffmpeg");
        foreach (var runtime in new[] { "win-x64", "win-arm64" })
        {
            await PublishStandalonePlugin(
                project,
                "kitopiaffmpeg",
                apiKey,
                runtime: runtime,
                prepareOutput: async output =>
                {
                    await BundleFfmpegAsync(runtime);
                    CopyDirectory(ArtifactsDirectory / "ffmpeg" / runtime / "tools" / "ffmpeg",
                        output / "tools" / "ffmpeg");
                });
        }
    }

    Task PublishSemanticSearchPlugin() => PublishStandalonePlugin(
        Solution.AllProjects.Single(project => project.Name == "KitopiaSemanticSearch"),
        "kitopiasemanticsearch",
        GetPluginApiKey("kitopiasemanticsearch"),
        runtime: null,
        uploadTargets: ["win-x64", "win-arm64", "osx-x64", "osx-arm64", "linux-x64", "linux-arm64"]);
}
