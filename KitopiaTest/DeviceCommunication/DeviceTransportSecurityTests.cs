using System.Net;
using System.Security.Cryptography;
using Core.Services.Config;
using Core.Services.DeviceCommunication.Discovery;
using Core.Services.DeviceCommunication.Security;
using ObservableCollections;
using PluginCore;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
[DoNotParallelize]
public sealed class DeviceTransportSecurityTests
{
    private Dictionary<string, PluginCore.Config.ConfigBase>? _originalConfigs;

    [TestInitialize]
    public void TestInitialize()
    {
        _originalConfigs = ConfigManger.Configs;
        ConfigManger.Configs = new Dictionary<string, PluginCore.Config.ConfigBase>(StringComparer.Ordinal)
        {
            ["KitopiaConfig"] = new KitopiaConfig { Name = "KitopiaConfig" }
        };
    }

    [TestCleanup]
    public void TestCleanup()
    {
        if (_originalConfigs is not null)
        {
            ConfigManger.Configs = _originalConfigs;
        }
    }

    [TestMethod]
    public void CreateIdentityCertificate_CreatesCertificateMatchingConfiguredDeviceIdentity()
    {
        var identity = CreateIdentity();
        ConfigManger.Config.devicePrivateKey = identity.PrivateKey;
        var security = new DeviceTransportSecurity(new FakeDeviceDiscoveryService());

        using var certificate = security.CreateIdentityCertificate("CN=Kitopia-Test");

        Assert.IsTrue(security.ValidateRemoteCertificate(certificate, identity.PublicKey));
    }

    [TestMethod]
    public void ValidateRemoteCertificate_ReturnsFalse_WhenExpectedIdentityDiffers()
    {
        var identity = CreateIdentity();
        var otherIdentity = CreateIdentity();
        ConfigManger.Config.devicePrivateKey = identity.PrivateKey;
        var security = new DeviceTransportSecurity(new FakeDeviceDiscoveryService());
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
        var security = new DeviceTransportSecurity(discoveryService);
        var mappedAddress = IPAddress.Parse("::ffff:192.168.1.20");

        var result = security.ResolveExpectedIdentityPublicKey(new IPEndPoint(mappedAddress, 22001));

        Assert.AreEqual("peer-public-key", result);
    }

    [TestMethod]
    public void ResolveExpectedIdentityPublicKey_ReturnsNull_WhenEndpointUnknown()
    {
        var security = new DeviceTransportSecurity(new FakeDeviceDiscoveryService());

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
