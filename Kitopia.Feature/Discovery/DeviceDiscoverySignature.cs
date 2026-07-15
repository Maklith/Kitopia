using System.Security.Cryptography;
using System.Text;

namespace Kitopia.Feature.DeviceCommunication.Discovery;

public static class DeviceDiscoverySignature
{
    private const int RsaKeySizeBits = 2048;
    public const long DefaultSignatureToleranceSeconds = 60;

    public static (string PublicKey, string PrivateKey) CreateKeyPair()
    {
        using var rsa = RSA.Create(RsaKeySizeBits);
        var publicKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        var privateKey = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());
        return (publicKey, privateKey);
    }

    public static bool TryDerivePublicKey(string? privateKey, out string publicKey)
    {
        publicKey = string.Empty;
        if (string.IsNullOrWhiteSpace(privateKey))
        {
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKey), out _);
            publicKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TrySign(DiscoveryInfo info, string? privateKey, out string signature)
    {
        signature = string.Empty;
        if (!TrySignData(BuildPayload(info), privateKey, out var signatureBytes))
        {
            return false;
        }

        signature = Convert.ToBase64String(signatureBytes);
        return true;
    }

    public static bool TrySignData(ReadOnlySpan<byte> data, string? privateKey, out byte[] signature)
    {
        signature = [];
        if (string.IsNullOrWhiteSpace(privateKey))
        {
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKey), out _);
            signature = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool Verify(DiscoveryInfo info)
    {
        if (string.IsNullOrWhiteSpace(info.Id) ||
            string.IsNullOrWhiteSpace(info.PublicKey) ||
            string.IsNullOrWhiteSpace(info.Signature))
        {
            return false;
        }

        try
        {
            var payload = BuildPayload(info);
            var signature = Convert.FromBase64String(info.Signature);
            var expectedId = ComputePublicKeyHash(info.PublicKey);
            if (!string.Equals(expectedId, info.Id, StringComparison.Ordinal))
            {
                return false;
            }

            return VerifyData(payload, info.PublicKey, signature);
        }
        catch
        {
            return false;
        }
    }

    public static bool VerifyAuthResponse(
        DiscoveryInfo info,
        string expectedNonce,
        long nowUnixSeconds,
        long toleranceSeconds = DefaultSignatureToleranceSeconds)
    {
        if (string.IsNullOrWhiteSpace(expectedNonce) ||
            !string.Equals(info.Nonce, expectedNonce, StringComparison.Ordinal))
        {
            return false;
        }

        var skew = nowUnixSeconds >= info.TimestampUnixSeconds
            ? nowUnixSeconds - info.TimestampUnixSeconds
            : info.TimestampUnixSeconds - nowUnixSeconds;

        return skew <= toleranceSeconds && Verify(info);
    }

    public static bool VerifyData(ReadOnlySpan<byte> data, string? publicKey, ReadOnlySpan<byte> signature)
    {
        if (string.IsNullOrWhiteSpace(publicKey) || signature.IsEmpty)
        {
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }

    public static string ComputePublicKeyHash(string? publicKey)
    {
        if (string.IsNullOrWhiteSpace(publicKey))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(publicKey.Trim());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static byte[] BuildPayload(DiscoveryInfo info)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(info.Id ?? string.Empty);
        writer.Write(info.Name ?? string.Empty);
        writer.Write(info.TcpPort);
        writer.Write(info.TimestampUnixSeconds);
        writer.Write(info.Nonce ?? string.Empty);
        writer.Flush();
        return stream.ToArray();
    }
}
