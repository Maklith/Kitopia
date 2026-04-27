using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Text.Json;
using Core.Services.DeviceCommunication;
using Core.Services.DeviceCommunication.Application;
using Core.Services.DeviceCommunication.Codecs;
using Core.Services.DeviceCommunication.Discovery;
using Core.Services.DeviceCommunication.Handlers;
using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Routing;
using Core.Services.DeviceCommunication.Sessions;
using ObservableCollections;
using PluginCore;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class MessageAppServiceTests
{
    [TestMethod]
    public async Task SendTextChatAsync_SerializesAndSendsEnvelope()
    {
        var listener = new FakeLocalDataListener();
        var service = CreateService(listener);

        var context = new MessageContext(
            LocalDataTransportProtocol.Tcp,
            new IPEndPoint(IPAddress.Loopback, 45000),
            "peer-key");

        await service.SendTextChatAsync(context, new TextChatMessage("peer-1", "hello"));

        Assert.AreEqual(1, listener.SendCount);
        Assert.IsNotNull(listener.LastPayload);
        var span = listener.LastPayload!.Value.Span;
        var envelopeLength = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4));
        var envelope = JsonSerializer.Deserialize<DataEnvelope>(span.Slice(16, envelopeLength));
        Assert.IsNotNull(envelope);
        Assert.AreEqual("chat", envelope.Route);
        Assert.AreEqual("text", envelope.Command);
    }

    [TestMethod]
    public void ResolveIncomingDisplayMode_WithoutActiveChatPage_NotifiesByToast()
    {
        var service = CreateService();

        var mode = service.ResolveIncomingDisplayMode(
            isMainWindowActive: false,
            isDeviceChatPageOpen: false,
            conversationId: "peer-1",
            selectedConversationId: null);

        Assert.AreEqual(IncomingMessageDisplayMode.NotifyByToast, mode);
    }

    [TestMethod]
    public void ResolveIncomingDisplayMode_WithSameSelectedConversation_ShowsInline()
    {
        var service = CreateService();

        var mode = service.ResolveIncomingDisplayMode(
            isMainWindowActive: true,
            isDeviceChatPageOpen: true,
            conversationId: "peer-1",
            selectedConversationId: "peer-1");

        Assert.AreEqual(IncomingMessageDisplayMode.ShowInCurrentConversation, mode);
    }

    [TestMethod]
    public void ResolveIncomingDisplayMode_AfterClosingChatPage_NotifiesByToast()
    {
        var service = CreateService();

        service.UpdateDisplayContext(
            isMainWindowActive: true,
            isDeviceChatPageOpen: true,
            selectedConversationId: "peer-1");
        Assert.AreEqual(
            IncomingMessageDisplayMode.ShowInCurrentConversation,
            service.ResolveIncomingDisplayMode("peer-1"));

        service.UpdateDisplayContext(
            isMainWindowActive: true,
            isDeviceChatPageOpen: false,
            selectedConversationId: "peer-1");

        Assert.AreEqual(
            IncomingMessageDisplayMode.NotifyByToast,
            service.ResolveIncomingDisplayMode("peer-1"));
    }

    [TestMethod]
    public void ResolveIncomingDisplayMode_MainWindowInactive_NotifiesByToast()
    {
        var service = CreateService();

        service.UpdateDisplayContext(
            isMainWindowActive: false,
            isDeviceChatPageOpen: true,
            selectedConversationId: "peer-1");

        Assert.AreEqual(
            IncomingMessageDisplayMode.NotifyByToast,
            service.ResolveIncomingDisplayMode("peer-1"));
    }

    [TestMethod]
    public async Task ReceiveAsync_FileCompleteEvent_PublishesTransferCompleted()
    {
        var listener = new FakeLocalDataListener();
        var incomingBuffer = new IncomingMessageBuffer();
        var service = CreateService(listener, incomingBuffer, new FileTransferSessionStore());

        var transferId = Guid.NewGuid();
        await incomingBuffer.PublishEventAsync(
            new IncomingMessageEvent(
                new FileCompleteChatMessage("peer-1", transferId),
                IncomingMessageEventType.TransferCompleted,
                transferId,
                4,
                4));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var enumerator = service.ReceiveAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        Assert.IsTrue(await enumerator.MoveNextAsync());

        var evt = enumerator.Current;
        Assert.IsInstanceOfType<FileCompleteChatMessage>(evt.Message);
        Assert.AreEqual(IncomingMessageEventType.TransferCompleted, evt.EventType);
        Assert.AreEqual(transferId, evt.TransferId);
    }

    [TestMethod]
    public async Task HandleAsync_FilePayload_PublishesProgressAndPayloadEvent()
    {
        var registry = new MessageCodecRegistry(new IMessageCodec[] { new FileChatMessageCodec() });
        var sink = new RecordingIncomingMessageSink();
        var sessionStore = new FileTransferSessionStore();
        var handler = new ChatRouteHandler(registry, sink, sessionStore);

        var transferId = Guid.NewGuid();
        var envelope = new DataEnvelope
        {
            Route = "chat",
            Command = "file",
            StreamType = DataStreamType.File,
            ChannelId = transferId,
            Metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["senderId"] = "peer-1",
                ["fileName"] = "sample.bin",
                ["length"] = "4"
            }
        };

        var payloadBytes = new byte[] { 1, 2, 3, 4 };
        var tempFile = Path.Combine(Path.GetTempPath(), $"kitopia-route-{transferId:D}.bin");
        sessionStore.TryAdd(new FileTransferSession
        {
            ConversationId = "peer-1",
            TransferId = transferId,
            FileName = "sample.bin",
            SizeBytes = payloadBytes.Length,
            ContentType = "application/octet-stream",
            State = FileTransferState.Accepted,
            SavePath = tempFile
        });
        var payloadReader = PipeReader.Create(new MemoryStream(payloadBytes, writable: false));

        try
        {
            await handler.HandleAsync(
                new MessageContext(LocalDataTransportProtocol.Tcp, new IPEndPoint(IPAddress.Loopback, 12345), "peer-1"),
                envelope,
                payloadReader);

            var progressEvent = sink.Events.FirstOrDefault(evt => evt.EventType == IncomingMessageEventType.TransferProgress);
            Assert.IsNotNull(progressEvent);
            Assert.AreEqual(transferId, progressEvent.TransferId);
            Assert.AreEqual(payloadBytes.LongLength, progressEvent.BytesTransferred);
            Assert.AreEqual(payloadBytes.LongLength, progressEvent.TotalBytes);

            var payloadEvent = sink.Events.LastOrDefault();
            Assert.IsNotNull(payloadEvent);
            Assert.AreEqual(IncomingMessageEventType.TransferCompleted, payloadEvent.EventType);
            Assert.IsInstanceOfType<FileCompleteChatMessage>(payloadEvent.Message);

            var saved = await File.ReadAllBytesAsync(tempFile);
            CollectionAssert.AreEqual(payloadBytes, saved);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [TestMethod]
    public async Task WaitForDecisionAsync_WhenDecisionArrivesBeforeWaiter_ReturnsImmediately()
    {
        var incomingBuffer = new IncomingMessageBuffer();
        var transferId = Guid.NewGuid();
        await incomingBuffer.PublishAsync(new FileRejectChatMessage("peer-1", transferId, "rejected_by_user"));

        var stopwatch = Stopwatch.StartNew();
        var decision = await incomingBuffer.WaitForDecisionAsync(transferId, TimeSpan.FromSeconds(3));
        stopwatch.Stop();

        Assert.AreEqual(TransferDecision.Rejected, decision);
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromMilliseconds(200));
    }

    [TestMethod]
    public async Task WaitForDecisionAsync_WhenAcceptArrivesBeforeWaiter_ReturnsImmediatelyAsAccepted()
    {
        var incomingBuffer = new IncomingMessageBuffer();
        var transferId = Guid.NewGuid();
        await incomingBuffer.PublishAsync(new FileAcceptChatMessage("peer-1", transferId));

        var stopwatch = Stopwatch.StartNew();
        var decision = await incomingBuffer.WaitForDecisionAsync(transferId, TimeSpan.FromSeconds(3));
        stopwatch.Stop();

        Assert.AreEqual(TransferDecision.Accepted, decision);
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromMilliseconds(200));
    }

    private static MessageAppService CreateService()
    {
        return CreateService(new FakeLocalDataListener());
    }

    private static MessageAppService CreateService(FakeLocalDataListener listener)
    {
        return CreateService(listener, new IncomingMessageBuffer(), new FileTransferSessionStore());
    }

    private static MessageAppService CreateService(
        FakeLocalDataListener listener,
        IncomingMessageBuffer incomingBuffer,
        FileTransferSessionStore sessionStore)
    {
        var sender = new ProtocolSender(listener);
        var registry = new MessageCodecRegistry(new IMessageCodec[] { new ChatMessageCodec() });
        return new MessageAppService(
            registry,
            sender,
            incomingBuffer,
            new ImageTransferPolicy(),
            sessionStore,
            new FakeDeviceDiscoveryService(),
            new FakeToastService());
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

        public Task StartAsync(CancellationToken token) => Task.CompletedTask;

        public Task StopAsync() => Task.CompletedTask;

        public void Dispose()
        {
            Devices.Dispose();
            _devicesView.Dispose();
        }
    }

    private sealed class FakeToastService : IToastService
    {
        public void Init()
        {
        }

        public Task Show(string header, string text, Avalonia.Controls.Notifications.NotificationType notificationType = Avalonia.Controls.Notifications.NotificationType.Information)
        {
            return Task.CompletedTask;
        }

        public Task Show(ToastRequest request)
        {
            return Task.CompletedTask;
        }

        public IToastProgressHandle ShowProgress(string header, string text,
            Avalonia.Controls.Notifications.NotificationType notificationType = Avalonia.Controls.Notifications.NotificationType.Information,
            double initialProgress = 0, bool isIndeterminate = false)
        {
            throw new NotSupportedException();
        }

        public void Unregister()
        {
        }
    }

    private sealed class FakeLocalDataListener : ILocalDataListener
    {
        public int TcpPort => 0;
        public int QuicPort => 0;
        public bool SupportsQuic => false;
        public int SendCount { get; private set; }
        public ReadOnlyMemory<byte>? LastPayload { get; private set; }

        public Task StartListeningAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task StopListeningAsync() => Task.CompletedTask;

        public Task SendAsync(LocalDataTransportProtocol protocol, ReadOnlyMemory<byte> payload, IPEndPoint remoteEndPoint,
            string? remoteIdentityPublicKey = null, CancellationToken token = default)
        {
            _ = protocol;
            _ = remoteEndPoint;
            _ = remoteIdentityPublicKey;
            _ = token;
            SendCount++;
            LastPayload = payload.ToArray();
            return Task.CompletedTask;
        }

        public Task SendAsync(LocalDataTransportProtocol protocol, PipeReader payloadReader, IPEndPoint remoteEndPoint,
            string? remoteIdentityPublicKey = null, CancellationToken token = default)
        {
            throw new NotSupportedException();
        }

        public Task SendAsync(LocalDataTransportProtocol protocol, Stream stream, IPEndPoint remoteEndPoint,
            string? remoteIdentityPublicKey = null, CancellationToken token = default)
        {
            _ = protocol;
            _ = remoteEndPoint;
            _ = remoteIdentityPublicKey;
            _ = token;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            SendCount++;
            LastPayload = memory.ToArray();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingIncomingMessageSink : IIncomingMessageSink
    {
        public List<IncomingMessageEvent> Events { get; } = [];

        public ValueTask PublishAsync(Core.Services.DeviceCommunication.Messages.AppMessage message,
            CancellationToken cancellationToken = default)
        {
            Events.Add(new IncomingMessageEvent(message));
            return ValueTask.CompletedTask;
        }

        public ValueTask PublishEventAsync(IncomingMessageEvent messageEvent,
            CancellationToken cancellationToken = default)
        {
            Events.Add(messageEvent);
            return ValueTask.CompletedTask;
        }
    }
}
