#region

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Core.CustomScenario;
using Core.Services.Config;
using Core.Services.HotKey;
using Core.Services.Interfaces;
using Core.Utils;
using Core.ViewModel.Windows;
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

namespace Core.Services.Plugin;

public class Plugin
{
    private static ILogger Logger = LogManager.Logger.ForContext<Plugin>();

    private readonly AssemblyLoadContextH _plugin;
    private IPlugin _pluginService;

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
                        if (HotKeyManager.HotKetImpl.Add(hotKeyModel,
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
        List<OnnxModelInfoWrapper> onnxModelInfos = new();
        List<Func<InputDataAnalyzeTimeFlags, string, IEnumerable<InputData>>> inputDataIdentifier = new();
        List<(Func<InputDataAnalyzeTimeFlags>, Func<IEnumerable<InputData>, IEnumerable<SearchViewItem>>)>
            inputDataAnalyzerActions = new();
        Dictionary<string, Func<IInferenceSession>> onnxRuntimes = new();
        PluginInfo = pluginInfo;
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
                var dependencyServiceProviders = new Dictionary<string, IServiceProvider>();
                if (pluginInfo.PluginBaseInfo.Dependencies != null)
                {
                    foreach (var dependency in pluginInfo.PluginBaseInfo.Dependencies)
                    {
                        if (dependency.Key == "Kitopia") continue;
                        if (PluginManager.GetEnablePlugins().TryGetValue(dependency.Key, out var plugin))
                        {
                            if (plugin.ServiceProvider != null)
                            {
                                dependencyServiceProviders.Add(dependency.Key, plugin.ServiceProvider);
                            }
                        }
                    }
                }
                _pluginService.OnEnabled(ServiceProvider, dependencyServiceProviders);
                break;
            }


        pluginMainScenarioMethodCategoryGroup.Name = PluginInfo.PluginBaseInfo.Name;

        foreach (var type in t)
        {
            if (type.BaseType == typeof(ConfigBase))
            {
                var instance = (ConfigBase)Activator.CreateInstance(type);
                instance.Name = $"{PluginInfo.ToPlgString()}#{type.FullName}";
                AddConfig($"{PluginInfo.ToPlgString()}#{type.FullName}", instance);
            }

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

                if (methodInfo.GetCustomAttribute<CaptureAttribute>() is { } captureAttribute)
                    captureActions.Add(new ScreenCaptureExMethod
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
                    });
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

        PluginOverall.OnnxModelInfos.Add(PluginInfo.ToPlgString(), onnxModelInfos);
        PluginOverall.OnnxRuntimes.Add(PluginInfo.ToPlgString(), onnxRuntimes);
        PluginOverall.SearchWindowInputDataIdentifies.Add(PluginInfo.ToPlgString(), inputDataIdentifier);
        PluginOverall.SearchWindowInputDataAnalyzers.Add(PluginInfo.ToPlgString(), inputDataAnalyzerActions);
        
        foreach (var func in inputDataAnalyzerActions)
        {
            var inputDataAnalyzeTimeFlags = func.Item1.Invoke();
            if ((inputDataAnalyzeTimeFlags & InputDataAnalyzeTimeFlags.PluginLoad) == 0) continue; // 如果当前时间标志不匹配，则跳过
            var enumerable = func.Item2.Invoke([new InputData()]).ToList();
            _searchViewItems.AddRange(enumerable);
        }
        foreach (var searchViewItem in _searchViewItems)
        {
            var searchWindowViewModel = ServiceManager.Services.GetService<SearchWindowViewModel>();
            searchWindowViewModel!.IndexCollection.TryAdd(searchViewItem.OnlyKey,searchViewItem);
        }
        

        if (pluginMainScenarioMethodCategoryGroup.Childrens.Count != 0)
            ScenarioMethodCategoryGroup.RootScenarioMethodCategoryGroup.Childrens.Add(PluginInfo.ToPlgString(),
                pluginMainScenarioMethodCategoryGroup);
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
        ConfigManger.RemoveConfig($"{PluginInfo.ToPlgString()}");

        PluginOverall.ScreenCaptureExMethods.Remove(PluginInfo.ToPlgString());
        PluginOverall.OnnxModelInfos.Remove(PluginInfo.ToPlgString());
        PluginOverall.OnnxRuntimes.Remove(PluginInfo.ToPlgString());
        PluginOverall.SearchWindowInputDataIdentifies.Remove(PluginInfo.ToPlgString());

        if (PluginOverall.SearchWindowInputDataAnalyzers.TryGetValue(PluginInfo.ToPlgString(), out var analyzers))
        {
            var searchWindowViewModel = ServiceManager.Services.GetService<SearchWindowViewModel>();
            if (searchWindowViewModel != null)
            {
                foreach (var analyzer in analyzers)
                {
                    searchWindowViewModel.RemoveAnalyzerIndex(analyzer);
                }
            }
        }
        PluginOverall.SearchWindowInputDataAnalyzers.Remove(PluginInfo.ToPlgString());
        ScenarioMethodCategoryGroup.RootScenarioMethodCategoryGroup.RemoveMethodsByPluginName(PluginInfo.ToPlgString());
        var keyValuePairs = CustomScenarioGlobe.Triggers.Where(e => e.Value.PluginInfo == PluginInfo.ToPlgString());
        foreach (var keyValuePair in keyValuePairs) CustomScenarioGlobe.Triggers.Remove(keyValuePair.Key);

        foreach (var searchViewItem in _searchViewItems)
        {
            ServiceManager.Services.GetService<SearchWindowViewModel>()!.IndexCollection.TryRemove(searchViewItem.OnlyKey,out _);
            ServiceManager.Services.GetService<SearchWindowViewModel>()!.Items.Remove(searchViewItem);
        }
        _searchViewItems.Clear();

        keyValuePairs = null;


        CustomScenarioManger.UnloadWhichUseThePlugin(PluginInfo.ToPlgString());

        _pluginService.OnDisabled();
        _pluginService = null;
        PluginInfo = null;
        ServiceProvider = null;

        _plugin.Unload();
        weakReference = new WeakReference(_plugin);
    }
}
