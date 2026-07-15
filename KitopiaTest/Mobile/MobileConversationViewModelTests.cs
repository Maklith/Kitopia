using Kitopia.Feature.DeviceCommunication.Discovery;
using Kitopia.Feature.DeviceCommunication.Security;
using Kitopia.Mobile.Services;
using Microsoft.Extensions.DependencyInjection;
using ObservableCollections;

namespace KitopiaTest.Mobile;

[TestClass]
public sealed class MobileConversationViewModelTests
{
    [TestMethod]
    public void MobileDeviceCommunicationSettings_BroadcastName_UsesPlatformDeviceName()
    {
        var settings = new MobileDeviceCommunicationSettings(
            new MobileConfigService(),
            new FakePlatformRuntimeFeatures("  Pixel 9 Pro  "));

        Assert.AreEqual("Pixel 9 Pro", settings.BroadcastName);
    }

    [TestMethod]
    public void MobileDeviceCommunicationSettings_OperatingSystemName_UsesPlatformValue()
    {
        var settings = new MobileDeviceCommunicationSettings(
            new MobileConfigService(),
            new FakePlatformRuntimeFeatures("Pixel 9 Pro", "Android"));

        Assert.AreEqual("Android", settings.OperatingSystemName);
    }

    [TestMethod]
    public void AddMobileCommunicationRuntime_RegistersEphemeralCertificateStoragePolicy()
    {
        var services = new ServiceCollection();
        services.AddMobileCommunicationRuntime();
        using var serviceProvider = services.BuildServiceProvider();

        var policy = serviceProvider.GetRequiredService<IDeviceCertificateStoragePolicy>();

        Assert.AreSame(EphemeralDeviceCertificateStoragePolicy.Instance, policy);
    }

    [TestMethod]
    public async Task MobileDeviceCommunicationHost_StartAsync_StartsRuntimeAndDiscovery()
    {
        var runtime = new FakeCommunicationRuntime();
        var discovery = new FakeDiscoveryService();
        var host = new MobileDeviceCommunicationHost(runtime, discovery);

        await host.StartAsync();

        Assert.AreEqual(1, runtime.StartCount);
        Assert.AreEqual(1, discovery.StartCount);
    }

    [TestMethod]
    public async Task MobileDeviceCommunicationHost_StopAsync_StopsDiscoveryAndRuntime()
    {
        var runtime = new FakeCommunicationRuntime();
        var discovery = new FakeDiscoveryService();
        var host = new MobileDeviceCommunicationHost(runtime, discovery);

        await host.StartAsync();
        await host.StopAsync();

        Assert.AreEqual(1, discovery.StopCount);
        Assert.AreEqual(1, runtime.StopCount);
    }

    [TestMethod]
    public async Task MobileDeviceCommunicationHost_StartAndStop_AreIdempotent()
    {
        var runtime = new FakeCommunicationRuntime();
        var discovery = new FakeDiscoveryService();
        var host = new MobileDeviceCommunicationHost(runtime, discovery);

        await host.StartAsync();
        await host.StartAsync();
        await host.StopAsync();
        await host.StopAsync();

        Assert.AreEqual(1, runtime.StartCount);
        Assert.AreEqual(1, discovery.StartCount);
        Assert.AreEqual(1, discovery.StopCount);
        Assert.AreEqual(1, runtime.StopCount);
    }

    [TestMethod]
    public async Task MobileDeviceCommunicationHost_DiscoveryStartFails_RollsBackAndCanRetry()
    {
        var runtime = new FakeCommunicationRuntime();
        var discovery = new FakeDiscoveryService { StartFailuresRemaining = 1 };
        var host = new MobileDeviceCommunicationHost(runtime, discovery);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.StartAsync());

        Assert.AreEqual(1, runtime.StartCount);
        Assert.AreEqual(1, runtime.StopCount);
        Assert.AreEqual(1, discovery.StartCount);
        Assert.AreEqual(1, discovery.StopCount);

        await host.StartAsync();

        Assert.AreEqual(2, runtime.StartCount);
        Assert.AreEqual(2, discovery.StartCount);
    }

    private sealed class FakeCommunicationRuntime : IMobileCommunicationRuntime
    {
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            StartCount++;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StopCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePlatformRuntimeFeatures : IMobilePlatformRuntimeFeatures
    {
        public FakePlatformRuntimeFeatures(string defaultDeviceName, string operatingSystemName = "Mobile")
        {
            DefaultDeviceName = defaultDeviceName;
            OperatingSystemName = operatingSystemName;
        }

        public string DefaultDeviceName { get; }
        public string OperatingSystemName { get; }

        public IMobileCommunicationRuntime WrapCommunicationRuntime(IMobileCommunicationRuntime innerRuntime)
        {
            return innerRuntime;
        }
    }

    private sealed class FakeDiscoveryService : IDeviceDiscoveryService
    {
        private readonly ObservableList<DiscoveredDevice> _source = [];
        private readonly ISynchronizedView<DiscoveredDevice, DiscoveredDevice> _view;

        public FakeDiscoveryService()
        {
            _view = _source.CreateView(device => device);
            Devices = _view.ToNotifyCollectionChanged();
        }

        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public int StartFailuresRemaining { get; set; }
        public NotifyCollectionChangedSynchronizedViewList<DiscoveredDevice> Devices { get; }

        public Task StartAsync(CancellationToken token)
        {
            _ = token;
            StartCount++;
            if (StartFailuresRemaining > 0)
            {
                StartFailuresRemaining--;
                throw new InvalidOperationException("discovery_start_failed");
            }

            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StopCount++;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Devices.Dispose();
            _view.Dispose();
        }
    }
}
