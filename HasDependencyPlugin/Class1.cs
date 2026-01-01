using HasDependencyPluginBase;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace HasDependencyPlugin;

public class Class1 : IPlugin
{
    

    
    public void OnEnabled(IServiceProvider serviceProvider, Dictionary<string, IServiceProvider> dependencyServiceProviders)
    {
        dependencyServiceProviders.TryGetValue("hasdependencypluginbase", out var baseProvider);
        var testBase = baseProvider?.GetService<TestBase>();
        testBase.TestMethod();
    }

    public void OnDisabled()
    {
    }

    public static IServiceProvider GetServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Class1>();
        return services
            .BuildServiceProvider();
    }
}