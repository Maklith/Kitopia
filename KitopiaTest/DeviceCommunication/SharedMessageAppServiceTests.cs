using System.IO.Pipelines;
using System.Net;
using Kitopia.Feature.DeviceCommunication;
using Kitopia.Feature.DeviceCommunication.Application;
using Kitopia.Feature.DeviceCommunication.Codecs;
using Kitopia.Feature.DeviceCommunication.Discovery;
using Kitopia.Feature.DeviceCommunication.Sessions;
using Kitopia.Feature.DeviceCommunication.Transport;
using ObservableCollections;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class SharedMessageAppServiceTests
{
    [TestMethod]
    public async Task AcceptFileAsync_WithWriteStreamFactory_RegistersNonLocalSaveTarget()
    {
        var listener = new RecordingLocalDataListener();
        using var discovery = new FakeDeviceDiscoveryService();
        discovery.AddDevice(new DiscoveredDevice
        {
            Id = "peer-1",
            Ipv4Address = IPAddress.Loopback,
            TcpPort = 45000
        });

        var sessionStore = new FileTransferSessionStore();
        var service = new MessageAppService(
            new MessageCodecRegistry(),
            new DeviceTransportService(listener, discovery),
            new IncomingMessageBuffer(),
            sessionStore);
        var transferId = Guid.NewGuid();
        await using var target = new MemoryStream();

        await service.AcceptFileAsync(
            "peer-1",
            transferId,
            "content://kitopia/shared.bin",
            _ => new ValueTask<Stream>(target));

        Assert.IsTrue(sessionStore.TryGet(transferId, out var session));
        Assert.AreEqual("content://kitopia/shared.bin", session.SavePath);
        Assert.IsNotNull(session.OpenWriteStreamAsync);
        Assert.AreSame(target, await session.OpenWriteStreamAsync(CancellationToken.None));
        Assert.AreEqual(1, listener.SendCount);
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

        public Task StartAsync(CancellationToken token) => Task.CompletedTask;

        public Task StopAsync() => Task.CompletedTask;

        public void AddDevice(DiscoveredDevice device)
        {
            _devicesSource.Add(device);
        }

        public void Dispose()
        {
            Devices.Dispose();
            _devicesView.Dispose();
        }
    }

    private sealed class RecordingLocalDataListener : ILocalDataListener
    {
        public int TcpPort => 0;
        public int SendCount { get; private set; }

        public Task StartListeningAsync(CancellationToken token = default) => Task.CompletedTask;

        public Task StopListeningAsync() => Task.CompletedTask;

        public async Task SendAsync(
            LocalDataTransportProtocol protocol,
            PipeReader payloadReader,
            IPEndPoint remoteEndPoint,
            string? remoteIdentityPublicKey = null,
            CancellationToken token = default)
        {
            while (true)
            {
                var result = await payloadReader.ReadAsync(token);
                payloadReader.AdvanceTo(result.Buffer.End);
                if (result.IsCompleted)
                {
                    break;
                }
            }

            SendCount++;
        }
    }
}
