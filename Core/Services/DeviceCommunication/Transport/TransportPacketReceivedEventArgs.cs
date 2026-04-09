using System;
using System.IO;
using Core.Services.DeviceCommunication.Protocol;
using PluginCore;

namespace Core.Services.DeviceCommunication.Transport;

public sealed class TransportPacketReceivedEventArgs : EventArgs
{
    public TransportPacketReceivedEventArgs(PacketMetadata packet, Stream payload, DeviceModel sender)
    {
        Packet = packet;
        Payload = payload;
        Sender = sender;
    }

    public PacketMetadata Packet { get; }
    public Stream Payload { get; }
    public DeviceModel Sender { get; }
}
