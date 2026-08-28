#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Kitopia.Desktop.Features.CustomScenario;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.HotKey;
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

    private readonly AssemblyLoadContextH _plugin;
    private IPlugin _pluginService;
    private bool _enabled;

    public IServiceProvider? ServiceProvider;
    internal AssemblyLoadContextH AssemblyLoadContext => _plugin;

    private void AddConfig(string key, ConfigBase configBase)
    {
        void SerializeConfigToFile(FileInfo fileInfo)
        {
            var j = JsonSerializer.Serialize(configBase, configBase.GetType(), ConfigManger.DefaultOptions);
            File.WriteAllText(fileInfo.FullName, j);
        }

        var retryFlag = false;
        retry:

        var configF = new FileInfo(KitopiaPaths.GetConfigFilePath(key));
        if (!configF.Exists) SerializeConfigToFile(configF);

        var json = File.ReadAllText(configF.FullName);
        if (string.IsNullOrWhiteSpace(json))
        {
            SerializeConfigToFile(configF);
            ServiceManager.Services.GetService<IToastService>()!.Show("警告", $"{configF.Name}配置文件加载失败，已还原到最初配置");
        }

        try
        {
            var deserializeObject =
                JsonSerializer.Deserialize(json, configBase.GetType(), ConfigManger.DefaultOptions)! as ConfigBase ??
                configBase;
            if (!ConfigManger.Configs.TryAdd(key, deserializeObject)) ConfigManger.Configs[key] = deserializeObject;

            deserializeObject.GetType()
                .BaseType?.GetField("Instance")?
                .SetValue(deserializeObject, deserializeObject);
            deserializeObject.AfterLoad();
        }
        catch (Exception e)
        {
            Logger.Error(e, "配置文件加载失败");

            SerializeConfigToFile(configF);

            if (!retryFlag)
            {
                retryFlag = true;
                goto retry;
            }

            ServiceManager.Services.GetService<IToastService>()!.Show("错误", $"{configF.Name}配置文件加载失败，请检查配置文件内容是否正确");
        }

        if (retryFlag)
            ServiceManager.Services.GetService<IToastService>()!.Show("警告", $"{configF.Name}配置文件加载失败，已还原到最初配置");
        configBase.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.Public)
            .ToList()
            .ForEach(x =>
            {
                if (x.GetCustomAttribute<ConfigField>() is { } configField)
                    if (configField.FieldType == ConfigFieldType.快捷键)
                    {
                        var hotKeyModel = (HotKeyModel)x.GetValue(configBase)!;

                        var value = configBase.GetType().GetProperty($"{x.Name}Action")
                            ?.GetValue(configBase, null);
                        if (value is null)
                        {
                            Logger.Warning( $"未找到快捷键 {hotKeyModel.SignName} 的触发方法 {configField.ActionName}，请确保方法存在且命名正确");
                            return;
                        }
                        if (ServiceManager.Services.GetService<IHotKetImpl>()!.Register(hotKeyModel,
                                (Action<HotKeyModel>)value))
                            ServiceManager.Services.GetService<IToastService>()!.Show(new DialogContent
                            {
                                Title = $"快捷键{hotKeyModel.SignName}设置失败",
                                Content = "请重新设置快捷键，按键与系统其他程序冲突",
                                CloseButtonText = "关闭"
                            }.ToToastRequest());
                    }
            });
    }

    private readonly List<SearchViewItem> _searchViewItems = new();
    public Plugin(PluginLocalInfo pluginInfo)
    {
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
        PluginInfo = pluginInfo;
        var featureSource = string.IsNullOrWhiteSpace(PluginInfo.PluginBaseInfo.Name)
            ? PluginInfo.ToPlgString()
            : PluginInfo.PluginBaseInfo.Name;
        foreach (var type in t)
            if (type.GetInterface("IPlugin") != null)
            {
                Logger.Debug($"加载插件:{PluginInfo.ToPlgString()}");
                //var instance = Activator.CreateInstance(type);
                var methodInfo = type.GetMethod("GetServiceProvider");
                ServiceProvider = (IServiceProvider)methodInfo
                    .Invoke(null, null);

                var service = ServiceProvider.GetService(type);
                _pluginService = (IPlugin)service;
                break;
            }


        // Load every configuration before resolving plugin services that may consume it.
        foreach (var type in t)
        {
            if (type.BaseType != typeof(ConfigBase)) continue;

            var instance = (ConfigBase)Activator.CreateInstance(type);
            instance.Name = $"{PluginInfo.ToPlgString()}#{type.FullName}";
            AddConfig(instance.Name, instance);
        }

        pluginMainScenarioMethodCategoryGroup.Name = PluginInfo.PluginBaseInfo.Name;

        foreach (var type in t)
        {
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

    public void Enable()
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
        _pluginService.OnEnabled(ServiceProvider!, dependencyServiceProviders);
    }

    private Assembly _dll => _plugin.Assembly;

    public PluginLocalInfo PluginInfo { set; get; }


    public Type? GetType(string typeName)
    {
        foreach (var pluginAssembly in _plugin.Assemblies)
            if (pluginAssembly.GetType(typeName) != null)
                return pluginAssembly.GetType(typeName);

        return null;
    }

    public bool IsPluginAssembly(Assembly assembly)
    {
        return _plugin.Assemblies.Any(x => x == assembly);
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


    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void UnloadByPluginInfo(string pluginInfoEx, out WeakReference weakReference)
    {
        var plugin = PluginManager.GetEnablePlugins().TryGetValue(pluginInfoEx, out var value) ? value : null;
        if (plugin is not null)
        {
            plugin.Unload(out weakReference);

            return;
        }

        weakReference = new WeakReference(null);
    }

    public void Unload(out WeakReference weakReference)
    {
        Logger.Debug($"卸载插件:{PluginInfo.ToPlgString()}");

        if (_enabled)
        {
            try
            {
                _pluginService.OnDisabled();
            }
            catch (Exception e)
            {
                Logger.Error(e, $"停用插件 {PluginInfo.ToPlgString()} 时发生错误");
            }
            finally
            {
                _enabled = false;
            }
        }

        ConfigManger.RemoveConfig($"{PluginInfo.ToPlgString()}");

        PluginOverall.ScreenCaptureExMethods.Remove(PluginInfo.ToPlgString());
        lock (PluginOverall.Features)
        {
            PluginOverall.Features.Remove(PluginInfo.ToPlgString());
        }
        PluginOverall.OnnxModelInfos.Remove(PluginInfo.ToPlgString());
        PluginOverall.OnnxRuntimes.Remove(PluginInfo.ToPlgString());
        PluginOverall.SearchWindowInputDataIdentifies.TryRemove(PluginInfo.ToPlgString(), out _);

        if (PluginOverall.SearchWindowInputDataAnalyzers.TryRemove(PluginInfo.ToPlgString(), out var analyzers))
        {
            var searchFeature = ServiceManager.Services.GetService<ISearchFeatureService>();
            if (searchFeature != null)
            {
                foreach (var analyzer in analyzers)
                {
                    searchFeature.RemoveAnalyzerIndex(analyzer);
                }
            }
        }
        ScenarioMethodCategoryGroup.RootScenarioMethodCategoryGroup.RemoveMethodsByPluginName(PluginInfo.ToPlgString());
        var keyValuePairs = CustomScenarioGlobe.Triggers
            .Where(e => e.Value.PluginInfo == PluginInfo.ToPlgString())
            .ToList();
        foreach (var keyValuePair in keyValuePairs) CustomScenarioGlobe.Triggers.Remove(keyValuePair.Key);

        ServiceManager.Services.GetService<ISearchFeatureService>()?.RemovePluginItems(_searchViewItems);
        _searchViewItems.Clear();

        keyValuePairs = null;


        CustomScenarioManger.UnloadWhichUseThePlugin(PluginInfo.ToPlgString());

        _pluginService = null;
        if (ServiceProvider is IDisposable disposable)
            disposable.Dispose();
        PluginInfo = null;
        ServiceProvider = null;

        _plugin.Unload();
        weakReference = new WeakReference(_plugin);
    }
}
