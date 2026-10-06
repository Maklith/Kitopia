using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Headless;
using Avalonia.Threading;
using Kitopia.Desktop.Services;
using Kitopia.Feature.DeviceCommunication.Application;
using Kitopia.Feature.DeviceCommunication.Discovery;
using Kitopia.Feature.DeviceCommunication.Messages.Chat;
using Microsoft.Extensions.DependencyInjection;
using ObservableCollections;
using PluginCore;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
[DoNotParallelize]
public sealed class DesktopIncomingMessageSinkTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Kitopia.Desktop.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

    [TestMethod]
    public async Task TransferToast_ReturnToConversationWithoutProgress_ClosesAndCanReappearInBackground()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(DesktopIncomingMessageSinkTests));
        await session.Dispatch(async () =>
        {
            using var discovery = new FakeDiscoveryService();
            var messages = new FakeMessageAppService();
            var toasts = new RecordingToastService();
            using var services = new ServiceCollection().AddSingleton<IMessageAppService>(messages).BuildServiceProvider();
            var sink = new DesktopIncomingMessageSink(new IncomingMessageBuffer(), discovery, toasts, null!, null!, services);
            var transferId = Guid.NewGuid();
            await Task.Run(async () => await sink.PublishEventAsync(TransferEvent("peer-1", transferId, FileTransferStatus.InProgress)));
            Dispatcher.UIThread.RunJobs();
            var first = toasts.Handles.Single();
            Assert.AreEqual(50d, first.Progress);

            messages.UpdateDisplayContext(true, true, "peer-1");
            await first.Closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(1, first.CloseCount);

            messages.UpdateDisplayContext(false, true, "peer-1");
            await sink.PublishEventAsync(TransferEvent("peer-1", transferId, FileTransferStatus.InProgress, 75));
            var second = toasts.Handles.Last();
            Assert.HasCount(2, toasts.Handles);
            Assert.AreEqual(75d, second.Progress);
            Assert.AreEqual(0, second.CloseCount);

            messages.UpdateDisplayContext(true, true, "peer-1");
            await second.Closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(1, second.CloseCount);
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task TransferToasts_ReturnToOneConversation_LeavesOtherConversationVisible()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(DesktopIncomingMessageSinkTests));
        await session.Dispatch(async () =>
        {
            using var discovery = new FakeDiscoveryService();
            var messages = new FakeMessageAppService();
            var toasts = new RecordingToastService();
            using var services = new ServiceCollection().AddSingleton<IMessageAppService>(messages).BuildServiceProvider();
            var sink = new DesktopIncomingMessageSink(new IncomingMessageBuffer(), discovery, toasts, null!, null!, services);
            await sink.PublishEventAsync(TransferEvent("peer-1", Guid.NewGuid(), FileTransferStatus.InProgress));
            await sink.PublishEventAsync(TransferEvent("peer-2", Guid.NewGuid(), FileTransferStatus.InProgress));

            messages.UpdateDisplayContext(true, true, "peer-1");
            await toasts.Handles[0].Closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(0, toasts.Handles[1].CloseCount);

            messages.UpdateDisplayContext(true, true, "peer-2");
            await toasts.Handles[1].Closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    [DataRow(FileTransferStatus.InProgress)]
    [DataRow(FileTransferStatus.Completed)]
    [DataRow(FileTransferStatus.Rejected)]
    [DataRow(FileTransferStatus.Cancelled)]
    [DataRow(FileTransferStatus.Failed)]
    [DataRow(FileTransferStatus.Timeout)]
    public async Task TransferToast_EventAfterReturningToConversation_ClosesWithoutNewNotification(FileTransferStatus status)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(DesktopIncomingMessageSinkTests));
        await session.Dispatch(async () =>
        {
            using var discovery = new FakeDiscoveryService();
            var messages = new FakeMessageAppService();
            var toasts = new RecordingToastService();
            using var services = new ServiceCollection().AddSingleton<IMessageAppService>(messages).BuildServiceProvider();
            var sink = new DesktopIncomingMessageSink(new IncomingMessageBuffer(), discovery, toasts, null!, null!, services);
            var transferId = Guid.NewGuid();
            await sink.PublishEventAsync(TransferEvent("peer-1", transferId, FileTransferStatus.InProgress));

            messages.UpdateDisplayContext(true, true, "peer-1");
            await sink.PublishEventAsync(TransferEvent("peer-1", transferId, status));

            var handle = toasts.Handles.Single();
            Assert.AreEqual(1, handle.CloseCount);
            Assert.AreEqual(0, handle.CompleteCount);
            Assert.AreEqual(0, handle.FailCount);
            Assert.HasCount(0, toasts.Requests);
            return true;
        }, CancellationToken.None);
    }

    [TestMethod]
    [DataRow(FileTransferStatus.Completed, FileTransferDirection.Download)]
    [DataRow(FileTransferStatus.Rejected, FileTransferDirection.Download)]
    [DataRow(FileTransferStatus.Cancelled, FileTransferDirection.Download)]
    [DataRow(FileTransferStatus.Cancelled, FileTransferDirection.Upload)]
    [DataRow(FileTransferStatus.Failed, FileTransferDirection.Download)]
    [DataRow(FileTransferStatus.Timeout, FileTransferDirection.Download)]
    public async Task TransferToast_TerminalEventInBackground_EndsProgress(FileTransferStatus status, FileTransferDirection direction)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(DesktopIncomingMessageSinkTests));
        await session.Dispatch(async () =>
        {
            using var discovery = new FakeDiscoveryService();
            var messages = new FakeMessageAppService();
            var toasts = new RecordingToastService();
            using var services = new ServiceCollection().AddSingleton<IMessageAppService>(messages).BuildServiceProvider();
            var sink = new DesktopIncomingMessageSink(new IncomingMessageBuffer(), discovery, toasts, null!, null!, services);
            var transferId = Guid.NewGuid();
            await sink.PublishEventAsync(TransferEvent("peer-1", transferId, FileTransferStatus.InProgress));
            await sink.PublishEventAsync(TransferEvent("peer-1", transferId, status) with { Direction = direction });

            var handle = toasts.Handles.Single();
            Assert.AreEqual(status == FileTransferStatus.Cancelled ? 1 : 0, handle.CloseCount);
            Assert.AreEqual(status == FileTransferStatus.Completed ? 1 : 0, handle.CompleteCount);
            Assert.AreEqual(status is FileTransferStatus.Rejected or FileTransferStatus.Failed or FileTransferStatus.Timeout ? 1 : 0,
                handle.FailCount);
            Assert.HasCount(0, toasts.Requests);
            return true;
        }, CancellationToken.None);
    }

    private static FileTransferUpdatedEvent TransferEvent(string conversationId, Guid transferId, FileTransferStatus status,
        long bytesTransferred = 50) => new(conversationId, transferId, FileTransferDirection.Download, status,
        "archive.zip", bytesTransferred, 100, null, DateTimeOffset.UtcNow);

    private sealed class RecordingProgressHandle : IToastProgressHandle
    {
        public double? Progress { get; private set; }
        public int CloseCount { get; private set; }
        public int CompleteCount { get; private set; }
        public int FailCount { get; private set; }
        public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Update(double? progress = null, string? text = null, string? header = null, bool? isIndeterminate = null)
            => Progress = progress;

        public void Complete(string? text = null, string? header = null, TimeSpan? autoCloseDelay = null) => CompleteCount++;
        public void Fail(string? text = null, string? header = null, TimeSpan? autoCloseDelay = null) => FailCount++;
        public void Close()
        {
            CloseCount++;
            Closed.TrySetResult();
        }
    }

    private sealed class RecordingToastService : IToastService
    {
        public List<RecordingProgressHandle> Handles { get; } = [];
        public List<ToastRequest> Requests { get; } = [];
        public void Init() { }
        public Task Show(string header, string text, NotificationType notificationType = NotificationType.Information,
            Window? dialogWindow = null) => throw new NotSupportedException();
        public Task Show(ToastRequest request, Window? dialogWindow = null)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
        public IToastProgressHandle ShowProgress(string header, string text, NotificationType notificationType,
            double initialProgress = 0, bool isIndeterminate = false)
        {
            var handle = new RecordingProgressHandle();
            Handles.Add(handle);
            return handle;
        }
        public bool HasUnreadSuppressedNotifications() => false;
        public bool TryOpenLatestSuppressedNotification() => false;
        public bool ShowSuppressedNotificationCenter() => false;
        public void ClearUnreadSuppressedNotifications() { }
        public void Unregister() { }
    }

    private sealed class FakeDiscoveryService : IDeviceDiscoveryService
    {
        private readonly ISynchronizedView<DiscoveredDevice, DiscoveredDevice> _devices =
            new ObservableList<DiscoveredDevice>().CreateView(device => device);

        public FakeDiscoveryService() => Devices = _devices.ToNotifyCollectionChanged();
        public NotifyCollectionChangedSynchronizedViewList<DiscoveredDevice> Devices { get; }
        public Task StartAsync(CancellationToken token) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public void Dispose()
        {
            Devices.Dispose();
            _devices.Dispose();
        }
    }

    private sealed class FakeMessageAppService : IMessageAppService
    {
        private string? _currentConversationId;
        public void UpdateDisplayContext(bool isMainWindowActive, bool isDeviceChatPageOpen, string? selectedConversationId)
            => _currentConversationId = isMainWindowActive && isDeviceChatPageOpen ? selectedConversationId : null;
        public IncomingMessageDisplayMode ResolveIncomingDisplayMode(string conversationId)
            => conversationId == _currentConversationId ? IncomingMessageDisplayMode.ShowInCurrentConversation : IncomingMessageDisplayMode.NotifyByToast;
        public IncomingMessageDisplayMode ResolveIncomingDisplayMode(bool isMainWindowActive, bool isDeviceChatPageOpen,
            string conversationId, string? selectedConversationId) => throw new NotSupportedException();
        public ValueTask SendTextChatAsync(string deviceId, string text, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public ValueTask SendFileChatAsync(string deviceId, FileChatMessage message, Stream stream, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public ValueTask SendImageChatAsync(string deviceId, ImageChatMessage message, Stream stream, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public ValueTask AcceptFileAsync(string deviceId, Guid transferId, string savePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public ValueTask AcceptFileAsync(string deviceId, Guid transferId, string displayPath,
            Func<CancellationToken, ValueTask<Stream>> openWriteStreamAsync, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public ValueTask RejectFileAsync(string deviceId, Guid transferId, string reason, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public ValueTask CancelTransferAsync(string deviceId, Guid transferId, string reason, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public IAsyncEnumerable<DeviceMessageEvent> ReceiveAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public void RequestOpenConversation(string conversationId) => throw new NotSupportedException();
        public string? GetRequestedConversationId() => null;
        public void ClearRequestedConversationId() { }
    }
}
