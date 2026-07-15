using Kitopia.Feature.DeviceCommunication.Discovery;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class SharedDiscoveryTests
{
    [TestMethod]
    public void CreateKeyPair_ThenDerivePublicKey_RoundTrips()
    {
        var (publicKey, privateKey) = DeviceDiscoverySignature.CreateKeyPair();

        var ok = DeviceDiscoverySignature.TryDerivePublicKey(privateKey, out var derivedPublicKey);

        Assert.IsTrue(ok);
        Assert.AreEqual(publicKey, derivedPublicKey);
    }

    [TestMethod]
    public void ComputePublicKeyHash_ReturnsStableNonEmptyHash()
    {
        var (publicKey, _) = DeviceDiscoverySignature.CreateKeyPair();

        var hash1 = DeviceDiscoverySignature.ComputePublicKeyHash(publicKey);
        var hash2 = DeviceDiscoverySignature.ComputePublicKeyHash(publicKey);

        Assert.IsFalse(string.IsNullOrWhiteSpace(hash1));
        Assert.AreEqual(hash1, hash2);
    }

    [TestMethod]
    public void TrySign_ThenVerify_RoundTripsAuthResponse()
    {
        var (publicKey, privateKey) = DeviceDiscoverySignature.CreateKeyPair();
        var info = CreateSignedInfoSkeleton(publicKey);

        var signed = DeviceDiscoverySignature.TrySign(info, privateKey, out var signature);
        info.Signature = signature;

        Assert.IsTrue(signed);
        Assert.IsTrue(DeviceDiscoverySignature.Verify(info));
        Assert.IsTrue(DeviceDiscoverySignature.VerifyAuthResponse(
            info,
            expectedNonce: info.Nonce,
            nowUnixSeconds: info.TimestampUnixSeconds));
    }

    [TestMethod]
    public void VerifyAuthResponse_ReturnsFalse_WhenNonceWrong()
    {
        var (publicKey, privateKey) = DeviceDiscoverySignature.CreateKeyPair();
        var info = CreateSignedInfoSkeleton(publicKey);
        DeviceDiscoverySignature.TrySign(info, privateKey, out var signature);
        info.Signature = signature;

        var ok = DeviceDiscoverySignature.VerifyAuthResponse(
            info,
            expectedNonce: "wrong-nonce",
            nowUnixSeconds: info.TimestampUnixSeconds);

        Assert.IsFalse(ok);
    }

    [TestMethod]
    public void VerifyAuthResponse_ReturnsFalse_WhenTimestampStale()
    {
        var (publicKey, privateKey) = DeviceDiscoverySignature.CreateKeyPair();
        var info = CreateSignedInfoSkeleton(publicKey);
        DeviceDiscoverySignature.TrySign(info, privateKey, out var signature);
        info.Signature = signature;

        var ok = DeviceDiscoverySignature.VerifyAuthResponse(
            info,
            expectedNonce: info.Nonce,
            nowUnixSeconds: info.TimestampUnixSeconds + 61);

        Assert.IsFalse(ok);
    }

    private static DiscoveryInfo CreateSignedInfoSkeleton(string publicKey)
    {
        return new DiscoveryInfo
        {
            MessageType = "auth.response",
            Version = "0.1",
            Id = DeviceDiscoverySignature.ComputePublicKeyHash(publicKey),
            Name = "peer-1",
            TcpPort = 22001,
            TimestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            PublicKey = publicKey,
            Nonce = Guid.NewGuid().ToString("N")
        };
    }
}
