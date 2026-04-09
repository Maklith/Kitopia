using System;
using System.IO;
using PluginCore;

namespace Core.Services.DeviceCommunication.Models;

public sealed class UdpReassemblySession
{
    public Guid SessionId { get; set; }
    public DeviceModel Sender { get; set; } = new();
    public MemoryStream DataStream { get; } = new();
    public string MetadataJson { get; set; } = string.Empty;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
}
