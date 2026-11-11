using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Media;

namespace KitopiaFfmpeg;

public sealed class KitopiaFfmpeg : IPlugin
{
    public void OnEnabled(IServiceProvider serviceProvider, Dictionary<string, IServiceProvider> dependencyServiceProviders) { }

    public void OnDisabled() { }

    public static IServiceProvider GetServiceProvider()
    {
        var directory = Path.Combine(Path.GetDirectoryName(typeof(KitopiaFfmpeg).Assembly.Location)!, "tools", "ffmpeg");
        var architectureDirectory = Path.Combine(directory, "win-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());
        if (Directory.Exists(architectureDirectory)) directory = architectureDirectory;
        return new ServiceCollection()
            .AddSingleton<KitopiaFfmpeg>()
            .AddSingleton(new Ffmpeg(directory))
            .BuildServiceProvider();
    }
}
