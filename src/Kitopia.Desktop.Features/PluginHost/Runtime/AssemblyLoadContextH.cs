#region

using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Avalonia;

#endregion

namespace Kitopia.Desktop.Features.Services.Plugin;

public class AssemblyLoadContextH : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly Dictionary<string, string> _dependencies;
    private Assembly? _assembly;
    private readonly string _pluginPath;

    public AssemblyLoadContextH(string pluginPath, string name, Dictionary<string, string> dependencies) : base(isCollectible: true, name: name)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
        _dependencies = dependencies;
        _pluginPath = pluginPath;
        Unloading += sender =>
        {
            _assembly = null;
            var types = sender.Assemblies.SelectMany(assembly => assembly.DefinedTypes).ToArray();
            var registry = AvaloniaPropertyRegistry.Instance;
            // Avalonia 12.1.2's UnregisterByModule leaves property and owner tables behind.
            // Keep this compatibility cleanup covered by collectible UI fixture tests.
            var registryType = typeof(AvaloniaPropertyRegistry);
            var registryGate = registryType.GetField("_unregisteringLocker", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(registry)!;
            lock (registryGate)
            {
                if (!registry.UnregisterByModule(types))
                    throw new InvalidOperationException("Avalonia plugin property metadata cleanup failed.");
                var properties = (Dictionary<int, AvaloniaProperty>)registryType
                    .GetField("_properties", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(registry)!;
                var propertyIds = properties.Where(pair => pair.Value is { } property && GetLoadContext(property.OwnerType.Assembly) == sender)
                    .Select(pair => pair.Key).ToArray();
                // FindRegistered indexes a dense ID table using Count. Preserve empty slots,
                // returning null for unloaded IDs without hiding later host/plugin properties.
                foreach (var id in propertyIds) properties[id] = null!;
                foreach (var field in new[] { "_registered", "_attached", "_direct" })
                {
                    var owners = (Dictionary<Type, Dictionary<int, AvaloniaProperty>>)registryType
                        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(registry)!;
                    foreach (var type in owners.Keys.Where(type => GetLoadContext(type.Assembly) == sender).ToArray())
                        owners.Remove(type);
                    foreach (var owner in owners.Values)
                        foreach (var id in propertyIds) owner.Remove(id);
                }
                foreach (var field in new[] { "_registeredCache", "_attachedCache", "_directCache", "_inheritedCache" })
                    ((Dictionary<Type, List<AvaloniaProperty>>)registryType
                        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(registry)!).Clear();
            }

            // Replacing JsonSerializerOptions does not release .NET 10's global emitted
            // member accessors. Reuse the runtime's cache invalidation implementation.
            typeof(JsonSerializer).Assembly.GetType("System.Text.Json.JsonSerializerOptionsUpdateHandler", throwOnError: true)!
                .GetMethod("ClearCache", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [types]);
        };
    }

    public Assembly Assembly => _assembly ??= LoadFromAssemblyPath(_pluginPath);

    protected override Assembly Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name ?? string.Empty;
        // Types crossing the SDK boundary must use the host's assembly identity.
        if (name is "PluginCore" or "Pinyin.NET" or "WinRT.Runtime" or "Microsoft.Windows.SDK.NET" or
            "OpenCvSharp" or "FFmpeg.AutoGen" or "Serilog" or "CommunityToolkit.Mvvm" or "Ursa" ||
            name.StartsWith("Avalonia", StringComparison.Ordinal) ||
            name.StartsWith("Irihi.", StringComparison.Ordinal) ||
            name.StartsWith("Semi.Avalonia", StringComparison.Ordinal) ||
            name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal))
        {
            try { return Default.LoadFromAssemblyName(assemblyName); }
            catch (FileNotFoundException) when (name != "PluginCore")
            {
                // Optional extensions absent from the host may still be private plugin dependencies.
            }
        }

        foreach (var dependency in _dependencies.Keys)
        {
            if (PluginManager.GetEnablePlugins().TryGetValue(dependency, out var plugin) &&
                plugin.AssemblyLoadContext.Assembly.GetName().Name == name)
                return plugin.AssemblyLoadContext.Assembly;
        }
        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath is not null) return LoadFromAssemblyPath(assemblyPath);

        // 如果本地未找到，尝试从依赖项中加载
        if (_dependencies != null)
        {
            foreach (var dependency in _dependencies)
            {
                // 跳过 Kitopia 核心依赖
                if (dependency.Key == "Kitopia") continue;

                if (PluginManager.GetEnablePlugins().TryGetValue(dependency.Key, out var plugin))
                {
                    try
                    {
                        var assembly = plugin.AssemblyLoadContext?.LoadFromAssemblyName(assemblyName);
                        if (assembly != null)
                        {
                            return assembly;
                        }
                    }
                    catch
                    {
                        // 忽略加载失败，继续尝试下一个依赖
                    }
                }
            }
        }

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        return IntPtr.Zero;
    }
}
