using System.Net;
using System.Security.Cryptography;
using Core.Services.DeviceCommunication.Discovery;
using Core.Services.DeviceCommunication.Security;
using Kitopia.DeviceCommunication.Identity;
using ObservableCollections;
using PluginCore;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class DeviceTransportSecurityTests
{
    [TestMethod]
    public void CreateIdentityCertificate_CreatesCertificateMatchingConfiguredDeviceIdentity()
    {
        var identity = CreateIdentity();
        var security = new DeviceTransportSecurity(new FakeDeviceDiscoveryService(), new FakeIdentityStore(identity));

        using var certificate = security.CreateIdentityCertificate("CN=Kitopia-Test");

        Assert.IsTrue(security.ValidateRemoteCertificate(certificate, identity.PublicKey));
    }

    [TestMethod]
    public void ValidateRemoteCertificate_ReturnsFalse_WhenExpectedIdentityDiffers()
    {
        var identity = CreateIdentity();
        var otherIdentity = CreateIdentity();
        var security = new DeviceTransportSecurity(new FakeDeviceDiscoveryService(), new FakeIdentityStore(identity));
        using var certificate = security.CreateIdentityCertificate("CN=Kitopia-Test");

        var result = security.ValidateRemoteCertificate(certificate, otherIdentity.PublicKey);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void ResolveExpectedIdentityPublicKey_MatchesIpv4MappedIpv6Endpoint()
    {
        var discoveryService = new FakeDeviceDiscoveryService();
        discoveryService.AddDevice(new DeviceModel
        {
            Id = "peer-public-key",
            Ipv4Address = IPAddress.Parse("192.168.1.20"),
            TcpPort = 22001
        });
        var security = new DeviceTransportSecurity(discoveryService, new FakeIdentityStore(CreateIdentity()));
        var mappedAddress = IPAddress.Parse("::ffff:192.168.1.20");

        var result = security.ResolveExpectedIdentityPublicKey(new IPEndPoint(mappedAddress, 22001));

        Assert.AreEqual("peer-public-key", result);
    }

    [TestMethod]
    public void ResolveExpectedIdentityPublicKey_ReturnsNull_WhenEndpointUnknown()
    {
        var security = new DeviceTransportSecurity(new FakeDeviceDiscoveryService(), new FakeIdentityStore(CreateIdentity()));

        var result = security.ResolveExpectedIdentityPublicKey(new IPEndPoint(IPAddress.Loopback, 22001));

        Assert.IsNull(result);
    }

    private static (string PublicKey, string PrivateKey) CreateIdentity()
    {
        using var rsa = RSA.Create(2048);
        var publicKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        var privateKey = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());
        return (publicKey, privateKey);
    }

    private sealed class FakeIdentityStore : IDeviceIdentityStore
    {
        private readonly DeviceIdentity _identity;

        public FakeIdentityStore((string PublicKey, string PrivateKey) identity)
        {
            _identity = new DeviceIdentity(
                identity.PublicKey,
                identity.PrivateKey,
                Kitopia.DeviceCommunication.Discovery.DeviceDiscoverySignature.ComputePublicKeyHash(identity.PublicKey));
        }

        public bool TryGetIdentity(out DeviceIdentity identity)
        {
            identity = _identity;
            return true;
        }

        public DeviceIdentity EnsureIdentity()
        {
            return _identity;
        }
    }

    private sealed class FakeDeviceDiscoveryService : IDeviceDiscoveryService
    {
        private readonly ObservableList<DeviceModel> _devicesSource = [];
        private readonly ISynchronizedView<DeviceModel, DeviceModel> _devicesView;

        public FakeDeviceDiscoveryService()
        {
            _devicesView = _devicesSource.CreateView(device => device);
            Devices = _devicesView.ToNotifyCollectionChanged();
        }

        public NotifyCollectionChangedSynchronizedViewList<DeviceModel> Devices { get; }

        public void AddDevice(DeviceModel device) => _devicesSource.Add(device);
        public Task StartAsync(CancellationToken token) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;

        public void Dispose()
        {
            Devices.Dispose();
            _devicesView.Dispose();
        }
    }
}
