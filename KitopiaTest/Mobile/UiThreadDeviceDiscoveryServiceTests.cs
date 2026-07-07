using Kitopia.DeviceCommunication.Discovery;
using Kitopia.Mobile.Services;
using ObservableCollections;

namespace KitopiaTest.Mobile;

[TestClass]
public sealed class UiThreadDeviceDiscoveryServiceTests
{
    [TestMethod]
    public void Constructor_MirrorsDiscoveredDeviceOperatingSystem()
    {
        using var innerService = new FakeDiscoveryService();
        innerService.AddDevice(new DiscoveredDevice
        {
            Id = "desktop-1",
            Name = "Desktop",
            OperatingSystem = "Windows"
        });

        using var service = new UiThreadDeviceDiscoveryService(innerService);

        Assert.AreEqual("Windows", service.Devices[0].OperatingSystem);
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

        public NotifyCollectionChangedSynchronizedViewList<DiscoveredDevice> Devices { get; }

        public void AddDevice(DiscoveredDevice device)
        {
            _source.Add(device);
        }

        public Task StartAsync(CancellationToken token) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;

        public void Dispose()
        {
            Devices.Dispose();
            _view.Dispose();
        }
    }
}
