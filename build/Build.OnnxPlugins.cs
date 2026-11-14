using System.Linq;
using System.Threading.Tasks;
using Fallout.Common;

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

    async Task PublishOnnxPlugins()
    {
        await PublishOnnxRuntimeCpu();
        await PublishOnnxRuntimeGpu();
        await PublishOnnxRuntimeOpenVino();
    }

    async Task PublishOnnxRuntimeCpu()
    {
        var project = Solution.AllProjects.Single(project => project.Name == "OnnxRuntime.CPU");
        var apiKey = GetPluginApiKey("kitopiaonnxruntimecpu");
        foreach (var runtime in new[] { "win-x64", "win-arm64" })
        {
            await PublishStandalonePlugin(project, "kitopiaonnxruntimecpu", apiKey, runtime: runtime);
        }
    }

    Task PublishOnnxRuntimeGpu() => PublishStandalonePlugin(
        Solution.AllProjects.Single(project => project.Name == "OnnxRuntime.Gpu.Win"),
        "kitopiaonnxruntimecuda",
        GetPluginApiKey("kitopiaonnxruntimecuda"),
        runtime: "win-x64");

    Task PublishOnnxRuntimeOpenVino() => PublishStandalonePlugin(
        Solution.AllProjects.Single(project => project.Name == "OnnxRuntime.OpenVino"),
        "kitopiaonnxruntimeopenvino",
        GetPluginApiKey("kitopiaonnxruntimeopenvino"),
        runtime: "win-x64");
}
