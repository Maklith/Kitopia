using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using Core.JsonConverter;
using Core.SDKs.Services;
using Core.SDKs.Services.Config;
using KitopiaEx;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Core.SDKs.CustomScenario.CustomScenarioValueSerializer;

public class INodeInputConnectorCustomScenarioValueSerializer: ICustomScenarioValueSerializer
{
    public string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value,ConfigManger.DefaultOptions);
    }

    public object Deserialize(ReadOnlySpan<byte> value)
    {
        var s = Encoding.UTF8.GetString(value);
        s = Regex.Unescape(s);
        var jsonNode = JsonNode.Parse(s);
        var realType = ServiceManager.Services.GetService<IPluginManger>().GetType(jsonNode["RealType"].ToString().Split(" "));
        var type = ServiceManager.Services.GetService<IPluginManger>().GetType(jsonNode["Type"].ToString().Split(" "));
        var valueS = jsonNode["Value"].ToString();
        var observableValue = JsonSerializer.Deserialize<ObservableValue>(valueS,ConfigManger.DefaultOptions);
        var instance = Activator.CreateInstance(realType);
        instance.GetType().GetProperty("Value").SetValue(instance,observableValue);
        return instance;
    }
}