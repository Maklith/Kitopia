using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace OnnxRuntime.Gpu.Win;

public class OnnxRuntimeGpuWin : IPlugin
{
    public void OnEnabled(IServiceProvider serviceProvider)
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