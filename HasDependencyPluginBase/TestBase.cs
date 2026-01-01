using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Attribute;

namespace HasDependencyPluginBase;

public class TestBase : IPlugin
{
    public void TestMethod()
    {
        Console.WriteLine(1);
    }

    public void OnEnabled(IServiceProvider serviceProvider, Dictionary<string, IServiceProvider> dependencyServiceProviders)
    {
    }

    public void OnDisabled()
    {
    }

    public static IServiceProvider GetServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestBase>();
        return services
            .BuildServiceProvider();
    }
}