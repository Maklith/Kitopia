using Kitopia.Desktop.Features.CustomScenario.CustomScenarioValueSerializer;
using Kitopia.Desktop.Features.Utils;
using PluginCore.CustomScenario;

namespace Kitopia.Desktop.Features.CustomScenario;

public static class CustomScenarioGlobe
{
    public static readonly Dictionary<string, string> I18N = new()
    {
        { typeof(string).FullName!, "字符串" },
        { typeof(bool).FullName!, "布尔" },
        { typeof(int).FullName!, "整数" },
        { typeof(double).FullName!, "浮点" },
        { typeof(object).FullName!, "任意" },
        { typeof(NodeConnectorClass).FullName!, "节点" }
    };

    public static readonly ObservableDictionary<string, CustomScenarioTriggerInfo> Triggers = new()
    {
        { "Kitopia_SoftwareStarted", new CustomScenarioTriggerInfo { Name = "Kitopia程序启动时" } },
        {
            "Kitopia_SoftwareShutdown",
            new CustomScenarioTriggerInfo { Name = "Kitopia程序关闭时", Description = "注意该触发器不会进入Tick" }
        }
    };

    public static Dictionary<Type, Func<object, string>> ToolTipConverters = new();

    public static Dictionary<Type, ICustomScenarioValueSerializer> JsonConverters = new()
    {
        { typeof(bool), new BoolCustomScenarioValueSerializer() },
        { typeof(NodeConnectorClass), new NodeConnectorClassCustomScenarioValueSerializer() },
        { typeof(int), new Int32CustomScenarioValueSerializer() },
        { typeof(double), new DoubleCustomScenarioValueSerializer() }
       
    };

    public static IEnumerable<CustomScenarioValueTuple> GetAllCouldUseTypeInValue
    {
        get
        {
            var valueTuples = new List<CustomScenarioValueTuple>();
            foreach (var keyValuePair in JsonConverters)
                valueTuples.Add(new CustomScenarioValueTuple
                {
                    Type = keyValuePair.Key,
                    Value = GetI18N(keyValuePair.Key.FullName)
                });

            return valueTuples;
        }
    }

    public static readonly Dictionary<string, Type> _baseType = new()
    {
        { "字符串", typeof(string) },
        { "布尔", typeof(bool) },
        { "整型", typeof(int) },
        { "双精度浮点数", typeof(double) }
    };

    public static string GetI18N(string key)
    {
        if (I18N.TryGetValue(key, out var n)) return n;

        return key;
    }
}
