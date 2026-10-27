using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Services.MQTT;

namespace Kitopia.Desktop.Features.PluginHost.Services;

public sealed class MqttStartupMessageBroker : IStartupMessageBroker
{
    public Task StopAsync()
    {
        return MqttManager.StopAsync();
    }
}
