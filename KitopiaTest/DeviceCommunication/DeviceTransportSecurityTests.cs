using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Kitopia.Feature.DeviceCommunication.Discovery;
using Kitopia.Feature.DeviceCommunication.Identity;
using Kitopia.Feature.DeviceCommunication.Security;
using Kitopia.Desktop.Services;
using ObservableCollections;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class DeviceTransportSecurityTests
{
    [TestMethod]
    public void CreateIdentityCertificate_CreatesCertificateMatchingConfiguredDeviceIdentity()
    {
        var identity = CreateIdentity();
        var security = new DeviceTransportSecurity(new FakeIdentityStore(identity));

        using var certificate = security.CreateIdentityCertificate("CN=Kitopia-Test");

        Assert.IsTrue(security.ValidateRemoteCertificate(certificate, identity.PublicKey));
    }

    [TestMethod]
    public void CreateIdentityCertificate_Pkcs12RoundTrip_PreservesIdentityAndPrivateKey()
    {
        var identity = CreateIdentity();
        var security = new DeviceTransportSecurity(
            new FakeIdentityStore(identity),
            EphemeralDeviceCertificateStoragePolicy.Instance);
        using var certificate = security.CreateIdentityCertificate("CN=Kitopia-RoundTrip");

        var pkcs12 = certificate.Export(X509ContentType.Pfx);
        using var reloadedCertificate = X509CertificateLoader.LoadPkcs12(
            pkcs12,
            password: null,
            X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);

        Assert.IsTrue(reloadedCertificate.HasPrivateKey);
        Assert.IsTrue(security.ValidateRemoteCertificate(reloadedCertificate, identity.PublicKey));
        using var privateKey = reloadedCertificate.GetRSAPrivateKey();
        Assert.IsNotNull(privateKey);
    }

    [TestMethod]
    public void CertificateStoragePolicies_ExposeExpectedBclKeyStorageFlags()
    {
        Assert.AreEqual(
            X509KeyStorageFlags.UserKeySet |
            X509KeyStorageFlags.PersistKeySet |
            X509KeyStorageFlags.Exportable,
            PersistedUserDeviceCertificateStoragePolicy.Instance.KeyStorageFlags);
        Assert.AreEqual(
            X509KeyStorageFlags.EphemeralKeySet |
            X509KeyStorageFlags.Exportable,
            EphemeralDeviceCertificateStoragePolicy.Instance.KeyStorageFlags);
    }

    [TestMethod]
    public void ValidateRemoteCertificate_ReturnsFalse_WhenExpectedIdentityDiffers()
    {
        var identity = CreateIdentity();
        var otherIdentity = CreateIdentity();
        var security = new DeviceTransportSecurity(new FakeIdentityStore(identity));
        using var certificate = security.CreateIdentityCertificate("CN=Kitopia-Test");

        var result = security.ValidateRemoteCertificate(certificate, otherIdentity.PublicKey);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void ResolveExpectedIdentityPublicKey_MatchesIpv4MappedIpv6Endpoint()
    {
        var discoveryService = new FakeDeviceDiscoveryService();
        discoveryService.AddDevice(new DiscoveredDevice
        {
            Id = "peer-public-key",
            Ipv4Address = IPAddress.Parse("192.168.1.20"),
            TcpPort = 22001
        });
        var resolver = new DesktopRemoteIdentityResolver(discoveryService);
        var mappedAddress = IPAddress.Parse("::ffff:192.168.1.20");

        var result = resolver.ResolveExpectedIdentityPublicKey(new IPEndPoint(mappedAddress, 22001));

        Assert.AreEqual("peer-public-key", result);
    }

    [TestMethod]
    public void ResolveExpectedIdentityPublicKey_ReturnsNull_WhenEndpointUnknown()
    {
        var resolver = new DesktopRemoteIdentityResolver(new FakeDeviceDiscoveryService());

        var result = resolver.ResolveExpectedIdentityPublicKey(new IPEndPoint(IPAddress.Loopback, 22001));

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
                Kitopia.Feature.DeviceCommunication.Discovery.DeviceDiscoverySignature.ComputePublicKeyHash(identity.PublicKey));
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
        private readonly ObservableList<DiscoveredDevice> _devicesSource = [];
        private readonly ISynchronizedView<DiscoveredDevice, DiscoveredDevice> _devicesView;

        public FakeDeviceDiscoveryService()
        {
            _devicesView = _devicesSource.CreateView(device => device);
            Devices = _devicesView.ToNotifyCollectionChanged();
        }

        public NotifyCollectionChangedSynchronizedViewList<DiscoveredDevice> Devices { get; }

        public void AddDevice(DiscoveredDevice device) => _devicesSource.Add(device);
        public Task StartAsync(CancellationToken token) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;

        public void Dispose()
        {
            Devices.Dispose();
            _devicesView.Dispose();
        }
    }
}
