#region

using System.Reflection;
using System.Runtime.Loader;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Avalonia;
using Kitopia.Desktop.Features.JsonConverter;
using Kitopia.Desktop.Features.Services.Config;

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
            ConfigManger.DefaultOptions = new JsonSerializerOptions
            {
                IncludeFields = true,
                WriteIndented = true,
                ReferenceHandler = ReferenceHandler.Preserve,
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                Converters = { new CustomScenarioInputValueJsonConverter(), new INodeInputJsonConverter() }
            };
            _assembly = null;
            foreach (var assembly in sender.Assemblies)
                AvaloniaPropertyRegistry.Instance.UnregisterByModule(assembly.DefinedTypes);

        };
    }

    public Assembly Assembly => _assembly ??= LoadFromAssemblyPath(_pluginPath);

    protected override Assembly Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name ?? string.Empty;
        // Types crossing the SDK boundary must use the host's assembly identity.
        if (name is "PluginCore" or "Pinyin.NET" or "WinRT.Runtime" or "Microsoft.Windows.SDK.NET" or
            "OpenCvSharp" or "Serilog" or "CommunityToolkit.Mvvm" or "Ursa" ||
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
