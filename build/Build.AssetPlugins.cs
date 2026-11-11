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

    Task PublishFfmpegPlugin() => PublishStandalonePlugin(
        Solution.AllProjects.Single(project => project.Name == "KitopiaFfmpeg"),
        "kitopiaffmpeg", GetPluginApiKey("kitopiaffmpeg"), runtime: null,
        prepareOutput: async output =>
        {
            // The market has one artifact per OS, so one Windows package carries both architectures.
            foreach (var runtime in new[] { "win-x64", "win-arm64" })
            {
                await BundleFfmpegAsync(runtime);
                CopyDirectory(ArtifactsDirectory / "ffmpeg" / runtime / "tools" / "ffmpeg",
                    output / "tools" / "ffmpeg" / runtime);
            }
        });

    Task PublishSemanticSearchPlugin() => PublishStandalonePlugin(
        Solution.AllProjects.Single(project => project.Name == "KitopiaSemanticSearch"),
        "kitopiasemanticsearch", GetPluginApiKey("kitopiasemanticsearch"), runtime: null);
}
