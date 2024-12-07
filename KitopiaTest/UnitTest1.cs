using MQTTnet;
using SharpHook;
using SharpHook.Native;

namespace KitopiaTest;

public class Tests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void Test1()
    {
        var mqttFactory = new MqttFactory();
        var mqttServerOptions = mqttFactory.CreateServerOptionsBuilder()
            .WithDefaultEndpoint().WithDefaultEndpointPort(6600).Build();
        var Server = mqttFactory.CreateMqttServer(mqttServerOptions);


        Server.StartAsync();
        Console.Read();


        Assert.Pass();
    }
}