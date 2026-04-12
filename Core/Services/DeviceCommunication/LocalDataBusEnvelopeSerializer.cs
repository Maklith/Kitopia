using System.Text.Json;

namespace Core.Services.DeviceCommunication;

internal static class LocalDataBusEnvelopeSerializer
{
    public static byte[] Serialize<TEnvelope>(TEnvelope envelope)
    {
        return JsonSerializer.SerializeToUtf8Bytes(envelope);
    }

    public static bool TryDeserialize<TEnvelope>(
        ReadOnlySpan<byte> payload,
        Func<TEnvelope, bool> isValid,
        out TEnvelope envelope)
        where TEnvelope : class, new()
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<TEnvelope>(payload);
            if (parsed is null || !isValid(parsed))
            {
                envelope = new TEnvelope();
                return false;
            }

            envelope = parsed;
            return true;
        }
        catch
        {
            envelope = new TEnvelope();
            return false;
        }
    }
}
