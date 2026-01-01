using Microsoft.Extensions.DependencyInjection;
using OnnxRuntime.OpenVino;
using PluginCore;

namespace OnnxRuntime.Gpu.Win;

public class OnnxRuntimeGpuWin : IPlugin
{
    public void OnEnabled(IServiceProvider serviceProvider, Dictionary<string, IServiceProvider> dependencyServiceProviders)
    {
    }

    public void OnDisabled()
    {
    }

    public static IServiceProvider GetServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<OnnxRuntimeGpuWin>();
        services.AddTransient<NPUOVInferenceSession>();
        services.AddTransient<GPUOVInferenceSession>();
        services.AddTransient<CPUOVInferenceSession>();
        return services.BuildServiceProvider();
    }
}