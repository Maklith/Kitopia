using Core.CustomScenario.CustomScenarioValueSerializer;
using Core.Utils;
using Core.ViewModel.TaskEditor;
using PluginCore;

namespace Core.CustomScenario;

public class CustomScenarioGloble
{
    public static Dictionary<string, string> _i18n = new()
    {
        { "System.String", "字符串" },
        { "System.Boolean", "布尔" },
        { "System.Int32", "整数" },
        { "System.Double", "浮点" },
        { "System.Object", "任意" },
        { "PluginCore.NodeConnectorClass", "节点" }
    };

    public static ObservableDictionary<string, CustomScenarioTriggerInfo> Triggers = new()
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
        { typeof(string), new StringCustomScenarioValueSerializer() },
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
        if (_i18n.TryGetValue(key, out var n)) return n;

        return key;
    }
}