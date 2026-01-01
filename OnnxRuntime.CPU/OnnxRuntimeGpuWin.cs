using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace OnnxRuntime.CPU;

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
        services.AddTransient<MInferenceSession>();
        return services.BuildServiceProvider();
    }
}