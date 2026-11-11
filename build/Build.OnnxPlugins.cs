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

    Task PublishOnnxRuntimeCpu() => PublishStandalonePlugin(
        Solution.AllProjects.Single(project => project.Name == "OnnxRuntime.CPU"),
        "kitopiaonnxruntimecpu",
        GetPluginApiKey("kitopiaonnxruntimecpu"));

    Task PublishOnnxRuntimeGpu() => PublishStandalonePlugin(
        Solution.AllProjects.Single(project => project.Name == "OnnxRuntime.Gpu.Win"),
        "kitopiaonnxruntimecuda",
        GetPluginApiKey("kitopiaonnxruntimecuda"));

    Task PublishOnnxRuntimeOpenVino() => PublishStandalonePlugin(
        Solution.AllProjects.Single(project => project.Name == "OnnxRuntime.OpenVino"),
        "kitopiaonnxruntimeopenvino",
        GetPluginApiKey("kitopiaonnxruntimeopenvino"));
}
