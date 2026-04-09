using System.IO;
using PluginCore;

namespace Core.Services.DeviceCommunication.Protocol;

public sealed record PacketContext(PacketMetadata Packet, Stream DataStream, DeviceModel Sender);
