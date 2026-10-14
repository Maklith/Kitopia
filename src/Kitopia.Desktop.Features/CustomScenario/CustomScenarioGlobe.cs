using Kitopia.Desktop.Features.CustomScenario.CustomScenarioValueSerializer;
using Kitopia.Desktop.Features.Utils;
using PluginCore.CustomScenario;

namespace Kitopia.Desktop.Features.CustomScenario;

public static class CustomScenarioGlobe
{
    public static readonly Dictionary<string, string> TypeNames = new()
    {
        { typeof(string).FullName!, "lang.kitopia.string" },
        { typeof(bool).FullName!, "lang.kitopia.boolean_type" },
        { typeof(int).FullName!, "lang.kitopia.types.integer" },
        { typeof(double).FullName!, "lang.kitopia.decimal" },
        { typeof(object).FullName!, "lang.kitopia.any" },
        { typeof(NodeConnectorClass).FullName!, "lang.kitopia.node_type" }
    };

    public static readonly ObservableDictionary<string, CustomScenarioTriggerInfo> Triggers = new()
    {
        { "Kitopia_SoftwareStarted", new CustomScenarioTriggerInfo { Name = "lang.kitopia.when_kitopia_starts" } },
        {
            "Kitopia_SoftwareShutdown",
            new CustomScenarioTriggerInfo { Name = "lang.kitopia.when_kitopia_exits", Description = "lang.kitopia.this_trigger_does_not_enter_tick" }
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
            foreach (var type in JsonConverters.Keys.Prepend(typeof(string)).Distinct())
            {
                if (type == typeof(NodeConnectorClass)) continue;
                yield return new CustomScenarioValueTuple
                {
                    Type = type,
                    Value = GetTypeNameKey(type.FullName!)
                };
            }
        }
    }

    public static readonly Dictionary<string, Type> _baseType = new()
    {
        { "字符串", typeof(string) },
        { "布尔", typeof(bool) },
        { "整型", typeof(int) },
        { "双精度浮点数", typeof(double) }
    };

    public static string GetTypeNameKey(string key)
    {
        if (TypeNames.TryGetValue(key, out var n)) return n;

        return key;
    }
}
