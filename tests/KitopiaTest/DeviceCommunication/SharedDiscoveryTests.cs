using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using Kitopia.Feature.DeviceCommunication.Discovery;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class SharedDiscoveryTests
{
    [TestMethod]
    [DataRow("192.0.2.10", true)]
    [DataRow("169.254.10.20", true)]
    [DataRow("2001:db8::10", true)]
    [DataRow("fe80::10%21", true)]
    [DataRow("0.0.0.0", false)]
    [DataRow("255.255.255.255", false)]
    [DataRow("127.0.0.1", false)]
    [DataRow("::", false)]
    [DataRow("::1", false)]
    public void DiscoveryAddress_UnicastOrWildcard_OnlyUsableSourcesAreSelected(string address, bool expected)
    {
        var method = typeof(DeviceDiscoveryService).GetMethod("IsUsableDiscoveryAddress",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var unicast = new TestUnicastAddress(IPAddress.Parse(address), DuplicateAddressDetectionState.Preferred);

        Assert.AreEqual(expected, method.Invoke(null, [unicast]));
    }

    [TestMethod]
    [DataRow(DuplicateAddressDetectionState.Preferred, true)]
    [DataRow(DuplicateAddressDetectionState.Tentative, false)]
    [DataRow(DuplicateAddressDetectionState.Duplicate, false)]
    [DataRow(DuplicateAddressDetectionState.Invalid, false)]
    [DataRow(DuplicateAddressDetectionState.Deprecated, false)]
    public void DiscoveryAddress_WindowsAddressReadiness_SkipsUnreadyAddresses(DuplicateAddressDetectionState state,
        bool expectedOnWindows)
    {
        var method = typeof(DeviceDiscoveryService).GetMethod("IsUsableDiscoveryAddress",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var unicast = new TestUnicastAddress(IPAddress.Parse("192.0.2.10"), state);

        Assert.AreEqual(!OperatingSystem.IsWindows() || expectedOnWindows, method.Invoke(null, [unicast]));
    }

    private sealed class TestUnicastAddress(IPAddress address, DuplicateAddressDetectionState state)
        : UnicastIPAddressInformation
    {
        public override IPAddress Address => address;
        public override DuplicateAddressDetectionState DuplicateAddressDetectionState => OperatingSystem.IsWindows()
            ? state : throw new PlatformNotSupportedException();
        public override bool IsDnsEligible => false;
        public override bool IsTransient => false;
        public override long AddressPreferredLifetime => throw new NotSupportedException();
        public override long AddressValidLifetime => throw new NotSupportedException();
        public override long DhcpLeaseLifetime => throw new NotSupportedException();
        public override IPAddress IPv4Mask => throw new NotSupportedException();
        public override PrefixOrigin PrefixOrigin => throw new NotSupportedException();
        public override SuffixOrigin SuffixOrigin => throw new NotSupportedException();
    }

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
