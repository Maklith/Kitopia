using System.IO;
using System.Security.Cryptography;
using System.Text;
using Core.Services.DeviceCommunication.Discovery;

namespace Core.Services.DeviceCommunication;

internal static class DeviceDiscoverySignature
{
    private const int RsaKeySizeBits = 2048;

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
        if (string.IsNullOrWhiteSpace(privateKey))
        {
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKey), out _);
            var payload = BuildPayload(info);
            signature = Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool Verify(DiscoveryInfo info)
    {
        if (string.IsNullOrWhiteSpace(info.Id) || string.IsNullOrWhiteSpace(info.Signature))
        {
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(info.Id), out _);
            var payload = BuildPayload(info);
            var signature = Convert.FromBase64String(info.Signature);
            return rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] BuildPayload(DiscoveryInfo info)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(info.Id ?? string.Empty);
        writer.Write(info.Name ?? string.Empty);
        writer.Write(info.UdpPort);
        writer.Write(info.QuicPort);
        writer.Write(info.SupportsQuic);
        writer.Write(info.TimestampUnixSeconds);
        writer.Flush();
        return stream.ToArray();
    }
}
