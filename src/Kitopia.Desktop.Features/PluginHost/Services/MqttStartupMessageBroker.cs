using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Services.MQTT;
using MQTTnet.Server;

namespace Kitopia.Desktop.Features.PluginHost.Services;

public sealed class MqttStartupMessageBroker : IStartupMessageBroker
{
    public Task StopAsync()
    {
        return MqttManager.Server is null
            ? Task.CompletedTask
            : MqttManager.Server.StopAsync(new MqttServerStopOptions());
    }
}
