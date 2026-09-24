using System.IO.Pipelines;
using System.Net;
using Kitopia.Feature.DeviceCommunication;
using Kitopia.Feature.DeviceCommunication.Application;
using Kitopia.Feature.DeviceCommunication.Codecs;
using Kitopia.Feature.DeviceCommunication.Discovery;
using Kitopia.Feature.DeviceCommunication.Messages.Chat;
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
        var incoming = new IncomingMessageBuffer();
        var service = new MessageAppService(
            new MessageCodecRegistry(),
            new DeviceTransportService(listener, discovery),
            incoming,
            sessionStore);
        var transferId = Guid.NewGuid();
        await using var target = new MemoryStream();

        await incoming.PublishAsync(new FileOfferChatMessage("peer-1", transferId, "shared.bin", 4,
            "application/octet-stream"));
        using var receiveCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using (var received = service.ReceiveAsync(receiveCancellation.Token).GetAsyncEnumerator())
        {
            Assert.IsTrue(await received.MoveNextAsync());
        }

        await service.AcceptFileAsync(
            "peer-1",
            transferId,
            "content://kitopia/shared.bin",
            _ => new ValueTask<Stream>(target));

        Assert.IsTrue(sessionStore.TryGet(transferId, out var session));
        Assert.AreEqual("content://kitopia/shared.bin", session.SavePath);
        Assert.AreEqual(4, session.SizeBytes);
        Assert.IsTrue(session.IsIncoming);
        Assert.IsNotNull(session.OpenWriteStreamAsync);
        Assert.AreSame(target, await session.OpenWriteStreamAsync(CancellationToken.None));
        Assert.AreEqual(2, listener.SendCount);
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
