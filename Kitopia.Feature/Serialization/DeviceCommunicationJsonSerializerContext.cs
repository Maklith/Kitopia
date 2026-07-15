using System.Text.Json.Serialization;
using Kitopia.Feature.DeviceCommunication.Discovery;
using Kitopia.Feature.DeviceCommunication.Protocol;

namespace Kitopia.Feature.DeviceCommunication.Serialization;

[JsonSerializable(typeof(DataEnvelope))]
[JsonSerializable(typeof(DiscoveryInfo))]
public partial class DeviceCommunicationJsonSerializerContext : JsonSerializerContext
{
}
