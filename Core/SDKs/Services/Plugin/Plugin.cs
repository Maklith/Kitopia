#region

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Core.JsonConverter;
using Core.SDKs.CustomScenario;
using Core.SDKs.HotKey;
using Core.SDKs.Services.Config;
using log4net;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Attribute;
using PluginCore.Attribute.Scenario;
using PluginCore.Config;

#endregion

namespace Core.SDKs.Services.Plugin;

public class Plugin
{
    private static readonly ILog Log = LogManager.GetLogger(nameof(Plugin));

    private AssemblyLoadContextH? _plugin;

    public IServiceProvider? ServiceProvider;

    public void AddConfig(string key, ConfigBase configBase)
    {
        var configF =
            new FileInfo($"{AppDomain.CurrentDomain.BaseDirectory}configs{Path.DirectorySeparatorChar}{key}.json");
        if (!configF.Exists)
        {
            var j = JsonSerializer.Serialize(configBase, configBase.GetType(), ConfigManger.DefaultOptions);
            File.WriteAllText(configF.FullName, j);
        }

        var json = File.ReadAllText(configF.FullName);
        try
        {
            var deserializeObject =
                JsonSerializer.Deserialize(json, configBase.GetType(), ConfigManger.DefaultOptions)! as ConfigBase ??
                configBase;
            if (!ConfigManger.Configs.TryAdd(key, deserializeObject)) ConfigManger.Configs[key] = deserializeObject;

            deserializeObject.GetType()
                .BaseType.GetField("Instance")
                .SetValue(deserializeObject, deserializeObject);
            deserializeObject.AfterLoad();
        }
        catch (Exception e)
        {
            Log.Error(e);
            Log.Error("配置文件加载失败");
        }

        configBase.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.Public)
            .ToList()
            .ForEach(x =>
            {
                if (x.GetCustomAttribute<ConfigField>() is { } configField)
                    if (configField.FieldType == ConfigFieldType.快捷键)
                    {
                        var hotKeyModel = (HotKeyModel)x.GetValue(configBase)!;

                        if (HotKeyManager.HotKetImpl.Add(hotKeyModel,
                                (Action<HotKeyModel>)configBase.GetType().GetProperty($"{x.Name}Action")
                                    .GetValue(configBase, null)))
                            ServiceManager.Services.GetService<IContentDialog>().ShowDialog(null, new DialogContent()
                            {
                                Title = $"快捷键{hotKeyModel.SignName}设置失败",
                                Content = "请重新设置快捷键，按键与系统其他程序冲突",
                                CloseButtonText = "关闭"
                            });
                    }
            });
    }

    public Plugin(PluginInfo pluginInfo)
    {
        _plugin = new AssemblyLoadContextH(pluginInfo.FullPath, pluginInfo.FullPath.Split(Path.DirectorySeparatorChar)
            .Last() + "_plugin");
        Log.Debug($"加载插件:{pluginInfo.FullPath}");
        var t = _dll.GetExportedTypes();
        //Dictionary<string, (MethodInfo, object)> methodInfos = new();
        ScenarioMethodCategoryGroup pluginMainScenarioMethodCategoryGroup = new();

        List<Func<string, SearchViewItem?>> searchViews = new();
        PluginInfo = pluginInfo;
        foreach (var type in t)
            if (type.GetInterface("IPlugin") != null)
            {
                Log.Debug($"加载插件:{PluginInfo.ToPlgString()}");
                //var instance = Activator.CreateInstance(type);
                var methodInfo = type.GetMethod("GetServiceProvider");
                ServiceProvider = (IServiceProvider)methodInfo
                    .Invoke(null, null);

                ((IPlugin)ServiceProvider.GetService(type)).OnEnabled(ServiceProvider);
                break;
            }

        ScenarioMethodCategoryGroup.RootScenarioMethodCategoryGroup.Childrens.Add(PluginInfo.ToPlgString(),
            pluginMainScenarioMethodCategoryGroup);
        pluginMainScenarioMethodCategoryGroup.Name = PluginInfo.Name;

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
                CustomScenarioGloble.Triggers.Add($"{PluginInfo.ToPlgString()}_{type.Name}",
                    customScenarioTriggerInfo);
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
                    if (methodInfo.GetParameters()[^1].ParameterType.FullName !=
                        "System.Threading.CancellationToken") continue;

                    var scenarioMethodInfo = new ScenarioMethod(methodInfo, PluginInfo, scenarioMethodAttribute,
                        ScenarioMethodType.插件方法, ServiceProvider);
                    scenarioMethodCategoryGroup.Methods.Add(scenarioMethodInfo.MethodTitle,
                        scenarioMethodInfo.GenerateNode());
                }

                if (methodInfo.GetCustomAttributes(typeof(SearchMethod))
                    .Any()) //搜索的切入方法
                    searchViews.Add(e =>
                    {
                        var invoke = methodInfo.Invoke(
                            ServiceProvider!.GetService(methodInfo.DeclaringType!),
                            new object?[] { e });
                        if (invoke is null) return null;

                        return (SearchViewItem)invoke;
                    });
            }
        }

        PluginOverall.SearchActions.Add(PluginInfo.ToPlgString(), searchViews);
    }

    public Assembly? _dll => _plugin.Assembly;

    public PluginInfo PluginInfo { set; get; }


    public Type? GetType(string typeName)
    {
        foreach (var pluginAssembly in _plugin.Assemblies)
            if (pluginAssembly.GetType(typeName) != null)
                return pluginAssembly.GetType(typeName);

        return null;
    }

    public Type GetType(Type type)
    {
        foreach (var pluginAssembly in _plugin.Assemblies)
        foreach (var type1 in pluginAssembly.GetTypes())
            if (type1 == type)
                return type1;

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
            return  name.Split(",");
           
        }).ToList();
        var typeName = typeNames[1..];
        var stringsList = typeName.Select(e =>
        {
            int index = 0;
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
    public static void UnloadByPluginInfo(string pluginInfoEx, out WeakReference weakReference)
    {
        if (PluginManager.EnablePlugin.ContainsKey(pluginInfoEx))
        {
            PluginManager.EnablePlugin[pluginInfoEx]
                .Unload(out weakReference);

            return;
        }

        weakReference = new WeakReference(null);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Load(PluginInfo pluginInfoEx)
    {
        PluginManager.EnablePlugin.Add(pluginInfoEx.ToPlgString(),
            new Plugin(pluginInfoEx));
    }

    public void Unload(out WeakReference weakReference)
    {
        Log.Debug($"卸载插件:{PluginInfo.ToPlgString()}");
        ConfigManger.RemoveConfig($"{PluginInfo.ToPlgString()}");

        PluginOverall.SearchActions.Remove($"{PluginInfo.ToPlgString()}");
        ScenarioMethodCategoryGroup.RootScenarioMethodCategoryGroup.RemoveMethodsByPluginName(PluginInfo.ToPlgString());
        var keyValuePairs = CustomScenarioGloble.Triggers.Where(e => e.Value.PluginInfo == PluginInfo.ToPlgString());
        foreach (var keyValuePair in keyValuePairs) CustomScenarioGloble.Triggers.Remove(keyValuePair.Key);

        keyValuePairs = null;


        CustomScenarioManger.UnloadByPlugStr(PluginInfo.ToPlgString());

        PluginInfo = null;
        ServiceProvider = null;

        _plugin.Unload();
        weakReference = new WeakReference(_plugin);
    }
}