#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Kitopia.Desktop.Features.CustomScenario;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Utils;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Config;
using PluginCore.CustomScenario;
using PluginCore.CustomScenario.Attribute;
using PluginCore.CustomScenario.Attribute.ConfigField;
using PluginCore.CustomScenario.Attribute.Scenario;
using PluginCore.Onnx;
using PluginCore.SearchWindow.InputData;
using PluginCore.SearchWindow.InputDataAnalyzer;
using Serilog;

#endregion

namespace Kitopia.Desktop.Features.Services.Plugin;

public class Plugin
{
    private static ILogger Logger = LogManager.Logger.ForContext<Plugin>();

    private AssemblyLoadContextH? _plugin;
    private IPlugin? _pluginService;
    private bool _enabled;

    public IServiceProvider? ServiceProvider { get; private set; }
    internal AssemblyLoadContextH AssemblyLoadContext => _plugin!;
    private readonly List<string> _hotKeyIds = new();

    private void AddConfig(string key, ConfigBase defaults)
    {
        var config = ConfigManger.LoadConfig(key, defaults);
        foreach (var field in config.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            if (field.GetCustomAttribute<ConfigField>() is not { FieldType: ConfigFieldType.快捷键 } attribute ||
                field.GetValue(config) is not HotKeyModel model) continue;
            var callback = config.GetType().GetProperty($"{field.Name}Action")?.GetValue(config) as Action<HotKeyModel>;
            if (callback is null && attribute.ActionName is { } actionName && config.invokes.TryGetValue(actionName, out var action))
                callback = action as Action<HotKeyModel>;
            if (callback is null)
                throw new InvalidOperationException($"未找到快捷键 {model.SignName} 的触发方法。");
            _hotKeyIds.Add(model.UUID);
            if (!ServiceManager.Services.GetRequiredService<IHotKetImpl>().Register(model, callback))
                ServiceManager.Services.GetService<IToastService>()?.Show("快捷键注册失败", model.SignName);
        }
    }

    private readonly List<SearchViewItem> _searchViewItems = new();
    public Plugin(PluginLocalInfo pluginInfo)
    {
        PluginInfo = pluginInfo;
    }

    internal void Load()
    {
        var pluginInfo = PluginInfo;
        _plugin = new AssemblyLoadContextH(pluginInfo.FullPath, pluginInfo.FullPath.Split(Path.DirectorySeparatorChar)
            .Last() + "_plugin", pluginInfo.PluginBaseInfo.Dependencies);
        Logger.Debug($"加载插件:{pluginInfo.FullPath}");
        var t = _dll.GetExportedTypes();
        //Dictionary<string, (MethodInfo, object)> methodInfos = new();
        ScenarioMethodCategoryGroup pluginMainScenarioMethodCategoryGroup = new();


        List<ScreenCaptureExMethod> captureActions = new();
        List<FeatureInfo> pluginFeatures = new();
        List<OnnxModelInfoWrapper> onnxModelInfos = new();
        List<Func<InputDataAnalyzeTimeFlags, string?, IEnumerable<InputData>>> inputDataIdentifier = new();
        List<(Func<InputDataAnalyzeTimeFlags>, Func<IEnumerable<InputData>, IEnumerable<SearchViewItem>>)>
            inputDataAnalyzerActions = new();
        Dictionary<string, Func<IInferenceSession>> onnxRuntimes = new();
        var featureSource = string.IsNullOrWhiteSpace(PluginInfo.PluginBaseInfo.Name)
            ? PluginInfo.ToPlgString()
            : PluginInfo.PluginBaseInfo.Name;
        var entryTypes = t.Where(type => type.IsClass && !type.IsAbstract &&
            typeof(IPlugin).IsAssignableFrom(type)).ToArray();
        if (entryTypes.Length != 1)
            throw new InvalidOperationException($"插件 {PluginInfo.ToPlgString()} 必须且只能提供一个 IPlugin 实现。");

        foreach (var type in t)
        {
            if (type.IsAbstract || !typeof(ConfigBase).IsAssignableFrom(type)) continue;
            var instance = (ConfigBase)Activator.CreateInstance(type)!;
            AddConfig($"{PluginInfo.ToPlgString()}#{type.FullName}", instance);
        }

        var entryType = entryTypes[0];
        var factory = entryType.GetMethod(nameof(IPlugin.GetServiceProvider), BindingFlags.Public | BindingFlags.Static)
                      ?? throw new InvalidOperationException($"插件 {entryType.FullName} 缺少 GetServiceProvider。");
        ServiceProvider = factory.Invoke(null, null) as IServiceProvider
                          ?? throw new InvalidOperationException($"插件 {entryType.FullName} 未提供服务容器。");
        _pluginService = ServiceProvider.GetRequiredService(entryType) as IPlugin
                         ?? throw new InvalidOperationException($"插件入口 {entryType.FullName} 未注册到服务容器。");

        pluginMainScenarioMethodCategoryGroup.Name = PluginInfo.PluginBaseInfo.Name;

        foreach (var type in t)
        {
            if (type.IsAbstract || type.IsInterface) continue;
            if (typeof(CustomScenarioTrigger).IsAssignableFrom(type))
            {
                var fieldInfo = type.GetField("Info");
                var customScenarioTriggerInfo = (CustomScenarioTriggerInfo)(fieldInfo is null
                    ? new CustomScenarioTriggerInfo { Name = $"{PluginInfo.ToPlgString()}_{type.Name}" }
                    : fieldInfo.GetValue(null)!);
                customScenarioTriggerInfo.PluginInfo = PluginInfo.ToPlgString();
                CustomScenarioGlobe.Triggers.Add($"{PluginInfo.ToPlgString()}_{type.Name}",
                    customScenarioTriggerInfo);
            }

            if (typeof(IInferenceSession).IsAssignableFrom(type))
            {
                var inferenceSession = (IInferenceSession)ServiceProvider.GetService(type);

                onnxRuntimes.Add(inferenceSession.Device, () => (IInferenceSession)ServiceProvider.GetService(type));
            }

            if (typeof(IInputDataAnalyzer).IsAssignableFrom(type))
            {
                var inferenceSession = (IInputDataAnalyzer)ServiceProvider.GetService(type);
                inputDataAnalyzerActions.Add(
                    (() => inferenceSession.AnalyzeTimeFlags,
                        inputData => inferenceSession.AnalyzeInputData(inputData)));
            }

            if (typeof(IInputDataIdentifier).IsAssignableFrom(type))
            {
                var inferenceSession = (IInputDataIdentifier)ServiceProvider.GetService(type);
                inputDataIdentifier.Add((timeFlag, filePath) => inferenceSession.IdentifyInputData(timeFlag, filePath));
            }


            var scenarioMethodCategoryGroup = pluginMainScenarioMethodCategoryGroup;
            if (type.GetCustomAttribute<ScenarioMethodCategoryAttribute>() is { } scenarioMethodCategoryAttribute)
                scenarioMethodCategoryGroup =
                    ScenarioMethodCategoryGroup.GetScenarioMethodCategoryGroupByAttribute(
                        scenarioMethodCategoryAttribute, pluginMainScenarioMethodCategoryGroup);
            foreach (var methodInfo in type.GetMethods())
            {
                if (methodInfo.GetCustomAttribute<ScenarioMethodAttribute>() is { } scenarioMethodAttribute) //情景的可用节点
                {
                    var parameterInfos = methodInfo.GetParameters();
                    if (parameterInfos.Length == 0) continue;
                    var parameterTypeFullName = parameterInfos[^1].ParameterType.FullName;
                    if (parameterTypeFullName !=
                        "System.Threading.CancellationToken" && !
                            parameterTypeFullName.StartsWith("System.Nullable`1[[System.Threading.CancellationToken,"))
                        continue;

                    var scenarioMethodInfo = new ScenarioMethod(methodInfo, PluginInfo, scenarioMethodAttribute,
                        ScenarioMethodType.PluginMethod, ServiceProvider);
                    scenarioMethodCategoryGroup.Methods.Add(scenarioMethodInfo.MethodTitle,
                        scenarioMethodInfo.GenerateNode());
                }

                if (methodInfo.GetCustomAttribute<FeatureAttribute>() is { } featureAttribute)
                {
                    pluginFeatures.Add(PluginOverall.CreateFeature(
                        featureSource,
                        methodInfo,
                        featureAttribute,
                        ServiceProvider));
                }

                if (methodInfo.GetCustomAttribute<CaptureAttribute>() is { } captureAttribute)
                {
                    var captureAction = new ScreenCaptureExMethod
                    {
                        Action = e =>
                        {
                            try
                            {
                                methodInfo.Invoke(
                                    ServiceProvider!.GetService(methodInfo.DeclaringType!),
                                    new object?[] { e });
                            }
                            catch (Exception exception)
                            {
                                ServiceManager.Services.GetService<IToastService>().Show("执行截图扩展方法时出现错误",
                                    exception.InnerException?.Message ?? exception.Message);
                                Logger.Error(exception, "错误");
                            }
                        },
                        Description = captureAttribute.Description,
                        Symbol = captureAttribute.Symbol
                    };
                    captureActions.Add(captureAction);
                }

            }

            foreach (var propertyInfo in type.GetProperties())
                if (propertyInfo.GetCustomAttribute<OnnxModelInfoAttribute>() is { } onnxModelInfoAttribute)
                {
                    var value = propertyInfo.GetValue(ServiceProvider!.GetService(propertyInfo.DeclaringType!));
                    if (value is OnnxModelInfo onnxModelInfo)
                    {
                        onnxModelInfo.ModelPath = $"{pluginInfo.Path}{onnxModelInfo.ModelPath}";
                        onnxModelInfos.Add(new OnnxModelInfoWrapper
                        {
                            Model = onnxModelInfo,
                            PluginStr = PluginInfo.ToPlgString()
                        });
                    }
                }
        }


        PluginOverall.ScreenCaptureExMethods.Add(PluginInfo.ToPlgString(), captureActions);
        lock (PluginOverall.Features)
        {
            PluginOverall.Features.Add(PluginInfo.ToPlgString(), pluginFeatures);
        }

        PluginOverall.OnnxModelInfos.Add(PluginInfo.ToPlgString(), onnxModelInfos);
        PluginOverall.OnnxRuntimes.Add(PluginInfo.ToPlgString(), onnxRuntimes);
        if (!PluginOverall.SearchWindowInputDataIdentifies.TryAdd(PluginInfo.ToPlgString(), inputDataIdentifier))
            throw new InvalidOperationException($"Input data identifiers are already registered for {PluginInfo.ToPlgString()}.");
        if (!PluginOverall.SearchWindowInputDataAnalyzers.TryAdd(PluginInfo.ToPlgString(), inputDataAnalyzerActions))
            throw new InvalidOperationException($"Input data analyzers are already registered for {PluginInfo.ToPlgString()}.");
        
        foreach (var func in inputDataAnalyzerActions)
        {
            var inputDataAnalyzeTimeFlags = func.Item1.Invoke();
            if ((inputDataAnalyzeTimeFlags & InputDataAnalyzeTimeFlags.PluginLoad) == 0) continue; // 如果当前时间标志不匹配，则跳过
            var enumerable = func.Item2.Invoke([new InputData()]).ToList();
            _searchViewItems.AddRange(enumerable);
        }
        ServiceManager.Services.GetService<ISearchFeatureService>()?.AddPluginItems(_searchViewItems);
        

        if (pluginMainScenarioMethodCategoryGroup.Childrens.Count != 0)
            ScenarioMethodCategoryGroup.RootScenarioMethodCategoryGroup.Childrens.Add(PluginInfo.ToPlgString(),
                pluginMainScenarioMethodCategoryGroup);
    }

    internal void Enable()
    {
        if (_enabled) return;

        var dependencyServiceProviders = new Dictionary<string, IServiceProvider>();
        if (PluginInfo.PluginBaseInfo.Dependencies != null)
        {
            foreach (var dependency in PluginInfo.PluginBaseInfo.Dependencies)
            {
                if (dependency.Key == "Kitopia") continue;
                if (PluginManager.GetEnablePlugins().TryGetValue(dependency.Key, out var plugin) &&
                    plugin.ServiceProvider != null)
                {
                    dependencyServiceProviders.Add(dependency.Key, plugin.ServiceProvider);
                }
            }
        }

        // Treat a partially completed callback as enabled so the unload path can still clean it up.
        _enabled = true;
        _pluginService!.OnEnabled(ServiceProvider!, dependencyServiceProviders);
    }

    private Assembly _dll => _plugin!.Assembly;

    public PluginLocalInfo PluginInfo { get; }


    public Type? GetType(string typeName)
    {
        foreach (var pluginAssembly in _plugin.Assemblies)
            if (pluginAssembly.GetType(typeName) != null)
                return pluginAssembly.GetType(typeName);

        return null;
    }

    public bool IsPluginAssembly(Assembly assembly)
    {
        return _plugin?.Assemblies.Any(x => x == assembly) == true;
    }

    public MethodInfo GetMethod(string methodAbsolutelyName)
    {
        var strings = methodAbsolutelyName.Split("#");
        var split = strings[2].Split("|");
        var typeJsonConverter = new TypeJsonConverter();
        var typeNames = split.Select(e =>
        {
            var name = e.Replace("[", ",").Replace("]", "");
            return name.Split(",");
        }).ToList();
        var typeName = typeNames[1..];
        var stringsList = typeName.Select(e =>
        {
            var index = 0;
            return typeJsonConverter.ParseType(e, ref index);
        }).ToList();

        return _dll.GetType(strings[1]).GetMethods().First(x =>
        {
            if (x.Name != split[0]) return false;

            var parameterInfos = x.GetParameters();
            if (parameterInfos.Length != split.Length - 1) return false;

            for (var index = 0; index < parameterInfos.Length; index++)
            {
                var parameterInfo = parameterInfos[index];
                if (parameterInfo.ParameterType != stringsList.ElementAt(index)) return false;
            }

            return true;
        });
    }


    public async ValueTask<(WeakReference Context, bool Succeeded)> UnloadAsync(CancellationToken cancellationToken = default)
    {
        var name = PluginInfo.ToPlgString();
        var succeeded = true;
        void Cleanup(Action action)
        {
            try { action(); }
            catch (Exception exception)
            {
                succeeded = false;
                Logger.Error(exception, "清理插件 {Plugin} 资源失败", name);
            }
        }

        foreach (var uuid in _hotKeyIds)
            Cleanup(() =>
            {
                var hotkeys = ServiceManager.Services.GetRequiredService<IHotKetImpl>();
                if (!hotkeys.Remove(uuid) && hotkeys.GetByUuid(uuid) is not null)
                    throw new InvalidOperationException($"快捷键 {uuid} 无法注销。");
            });
        _hotKeyIds.Clear();
        Cleanup(() => CustomScenarioManger.UnloadWhichUseThePlugin(name));
        PluginOverall.ScreenCaptureExMethods.Remove(name);
        lock (PluginOverall.Features) PluginOverall.Features.Remove(name);
        PluginOverall.OnnxModelInfos.Remove(name);
        PluginOverall.OnnxRuntimes.Remove(name);
        PluginOverall.SearchWindowInputDataIdentifies.TryRemove(name, out _);
        if (PluginOverall.SearchWindowInputDataAnalyzers.TryRemove(name, out var analyzers))
        {
            foreach (var analyzer in analyzers)
                Cleanup(() => ServiceManager.Services.GetService<ISearchFeatureService>()?.RemoveAnalyzerIndex(analyzer));
        }
        Cleanup(() => ScenarioMethodCategoryGroup.RootScenarioMethodCategoryGroup.RemoveMethodsByPluginName(name));
        foreach (var trigger in CustomScenarioGlobe.Triggers.Where(pair => pair.Value.PluginInfo == name)
                     .Select(pair => pair.Key).ToArray())
            CustomScenarioGlobe.Triggers.Remove(trigger);
        Cleanup(() => ServiceManager.Services.GetService<ISearchFeatureService>()?.RemovePluginItems(_searchViewItems));
        _searchViewItems.Clear();

        if (_enabled && _pluginService is not null)
        {
            try { await _pluginService.OnDisabledAsync(cancellationToken); }
            catch (Exception exception) { succeeded = false; Logger.Error(exception, "停用插件 {Plugin} 失败", name); }
            _enabled = false;
        }
        Cleanup(() => ConfigManger.RemoveConfig(name));
        // Remove plugin-owned converter keys even if OnDisabled failed before unregistering them.
        foreach (var type in CustomScenarioGlobe.ToolTipConverters.Where(pair =>
                     IsPluginAssembly(pair.Key.Assembly) || IsPluginAssembly(pair.Value.Method.Module.Assembly) ||
                     pair.Value.Target is { } target && IsPluginAssembly(target.GetType().Assembly))
                     .Select(pair => pair.Key).ToArray())
            CustomScenarioGlobe.ToolTipConverters.Remove(type);
        foreach (var type in CustomScenarioGlobe.JsonConverters.Where(pair =>
                     IsPluginAssembly(pair.Key.Assembly) || IsPluginAssembly(pair.Value.GetType().Assembly))
                     .Select(pair => pair.Key).ToArray())
            CustomScenarioGlobe.JsonConverters.Remove(type);
        _pluginService = null;
        try
        {
            if (ServiceProvider is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
            else if (ServiceProvider is IDisposable disposable) disposable.Dispose();
        }
        catch (Exception exception) { succeeded = false; Logger.Error(exception, "释放插件 {Plugin} 服务容器失败", name); }
        finally { ServiceProvider = null; }

        var context = _plugin;
        _plugin = null;
        var weakReference = new WeakReference(context, trackResurrection: true);
        if (context is not null) Cleanup(context.Unload);
        return (weakReference, succeeded);
    }
}
