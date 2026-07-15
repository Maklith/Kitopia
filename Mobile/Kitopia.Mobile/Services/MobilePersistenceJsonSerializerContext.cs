using System.Text.Json.Serialization;

namespace Kitopia.Mobile.Services;

[JsonSerializable(
    typeof(Dictionary<string, string>),
    TypeInfoPropertyName = "StringDictionary")]
[JsonSerializable(
    typeof(MobileDeviceIdentityPayload),
    TypeInfoPropertyName = "DeviceIdentityPayload")]
internal partial class MobilePersistenceJsonSerializerContext : JsonSerializerContext
{
}
