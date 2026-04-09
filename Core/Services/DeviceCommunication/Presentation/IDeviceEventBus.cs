using System;
using PluginCore;

namespace Core.Services.DeviceCommunication.Presentation;

public interface IDeviceEventBus
{
    event EventHandler<DeviceCommunicationEventArgs>? CommunicationEvent;
    void Publish(DeviceCommunicationEventType type, EventArgs payload);
}
