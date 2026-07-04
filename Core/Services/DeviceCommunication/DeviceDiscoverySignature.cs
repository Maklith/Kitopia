using Core.Services.DeviceCommunication.Discovery;

namespace Core.Services.DeviceCommunication;

internal static class DeviceDiscoverySignature
{
    public static (string PublicKey, string PrivateKey) CreateKeyPair()
    {
        return Kitopia.DeviceCommunication.Discovery.DeviceDiscoverySignature.CreateKeyPair();
    }

    public static bool TryDerivePublicKey(string? privateKey, out string publicKey)
    {
        return Kitopia.DeviceCommunication.Discovery.DeviceDiscoverySignature.TryDerivePublicKey(privateKey, out publicKey);
    }

    public static bool TrySign(DiscoveryInfo info, string? privateKey, out string signature)
    {
        var sharedInfo = ToSharedInfo(info);
        return Kitopia.DeviceCommunication.Discovery.DeviceDiscoverySignature.TrySign(sharedInfo, privateKey, out signature);
    }

    public static bool TrySignData(ReadOnlySpan<byte> data, string? privateKey, out byte[] signature)
    {
        return Kitopia.DeviceCommunication.Discovery.DeviceDiscoverySignature.TrySignData(data, privateKey, out signature);
    }

    public static bool Verify(DiscoveryInfo info)
    {
        return Kitopia.DeviceCommunication.Discovery.DeviceDiscoverySignature.Verify(ToSharedInfo(info));
    }

    public static bool VerifyAuthResponse(
        DiscoveryInfo info,
        string expectedNonce,
        long nowUnixSeconds,
        long toleranceSeconds = Kitopia.DeviceCommunication.Discovery.DeviceDiscoverySignature.DefaultSignatureToleranceSeconds)
    {
        return Kitopia.DeviceCommunication.Discovery.DeviceDiscoverySignature.VerifyAuthResponse(
            ToSharedInfo(info),
            expectedNonce,
            nowUnixSeconds,
            toleranceSeconds);
    }

    public static bool VerifyData(ReadOnlySpan<byte> data, string? publicKey, ReadOnlySpan<byte> signature)
    {
        return Kitopia.DeviceCommunication.Discovery.DeviceDiscoverySignature.VerifyData(data, publicKey, signature);
    }

    public static string ComputePublicKeyHash(string? publicKey)
    {
        return Kitopia.DeviceCommunication.Discovery.DeviceDiscoverySignature.ComputePublicKeyHash(publicKey);
    }

    private static Kitopia.DeviceCommunication.Discovery.DiscoveryInfo ToSharedInfo(DiscoveryInfo info)
    {
        return new Kitopia.DeviceCommunication.Discovery.DiscoveryInfo
        {
            MessageType = info.MessageType,
            Version = info.Version,
            Id = info.Id,
            Name = info.Name,
            TcpPort = info.TcpPort,
            TimestampUnixSeconds = info.TimestampUnixSeconds,
            Signature = info.Signature,
            PublicKey = info.PublicKey,
            Nonce = info.Nonce
        };
    }
}
