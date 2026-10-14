using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Kitopia.Desktop.Features.JsonConverter;
using PluginCore.CustomScenario;

namespace KitopiaTest.CustomScenario;

[TestClass]
public sealed class CustomScenarioValueJsonConverterTests
{
    [TestMethod]
    [DataRow(typeof(object[]))]
    [DataRow(typeof(string[][]))]
    [DataRow(typeof(int[,]))]
    public void TypeJsonConverter_ArrayType_PreservesArraySuffix(Type type)
    {
        var previousServices = ServiceManager.Services;
        using var services = new ServiceCollection().BuildServiceProvider();
        try
        {
            ServiceManager.Services = services;
            var options = new JsonSerializerOptions { Converters = { new TypeJsonConverter() } };
            var json = JsonSerializer.Serialize(type, options);
            Assert.AreEqual(type, JsonSerializer.Deserialize<Type>(json, options));
        }
        finally
        {
            ServiceManager.Services = previousServices;
        }
    }
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new CustomScenarioInputValueJsonConverter() }
    };

    [TestMethod]
    public void Read_ValueBeforeType_DoesNotConsumeFollowingItem()
    {
        const string json = """
                            [
                              {"Value":"42","IsSelf":true,"ShowType":"System System.Int32","SerializeType":"System System.Int32"},
                              {"SerializeType":"System System.String","IsSelf":true,"Value":"next"}
                            ]
                            """;

        var values = JsonSerializer.Deserialize<CustomScenarioValue[]>(json, Options);

        Assert.IsNotNull(values);
        Assert.HasCount(2, values);
        Assert.AreEqual(42, values[0].Value);
        Assert.AreEqual(typeof(int), values[0].ShowType);
        Assert.AreEqual("next", values[1].Value);
    }

    [TestMethod]
    public void Read_NullValue_PreservesDeclaredType()
    {
        const string json = """{"SerializeType":"System System.Int32","IsSelf":true,"Value":null}""";

        var value = JsonSerializer.Deserialize<CustomScenarioValue>(json, Options);

        Assert.IsNotNull(value);
        Assert.AreEqual(typeof(int), value.SerializeType);
        Assert.IsNull(value.Value);
    }
}
