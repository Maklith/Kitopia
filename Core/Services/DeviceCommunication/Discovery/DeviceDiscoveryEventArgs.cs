using System;
using PluginCore;

namespace Core.Services.DeviceCommunication.Discovery;

public sealed class DeviceDiscoveryEventArgs : EventArgs
{
    public DeviceDiscoveryEventArgs(DeviceModel device)
    {
        Device = device;
    }

    public DeviceModel Device { get; }
}
