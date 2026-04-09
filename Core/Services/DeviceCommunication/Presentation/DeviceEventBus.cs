using System;
using PluginCore;

namespace Core.Services.DeviceCommunication.Presentation;

public sealed class DeviceEventBus : IDeviceEventBus
{
    public event EventHandler<DeviceCommunicationEventArgs>? CommunicationEvent;

    public void Publish(DeviceCommunicationEventType type, EventArgs payload)
    {
        var handlers = CommunicationEvent;
        if (handlers is null)
        {
            return;
        }

        var args = new DeviceCommunicationEventArgs(type, payload);
        foreach (var handler in handlers.GetInvocationList())
        {
            if (handler is not EventHandler<DeviceCommunicationEventArgs> typedHandler)
            {
                continue;
            }

            try
            {
                typedHandler(this, args);
            }
            catch
            {
            }
        }
    }
}
