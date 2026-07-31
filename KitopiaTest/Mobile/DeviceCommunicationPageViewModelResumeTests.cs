using Kitopia.Feature.DeviceCommunication.Application;
using Kitopia.Feature.Avalonia.DeviceCommunication.ViewModels;
using Kitopia.Feature.DeviceCommunication.Discovery;
using Kitopia.Feature.DeviceCommunication.Messages.Chat;
using Kitopia.Feature.DeviceCommunication.Messages.Clipboard;
using Kitopia.Mobile.Services;
using Kitopia.Mobile.ViewModels;
using ObservableCollections;

namespace KitopiaTest.Mobile;

[TestClass]
public sealed class DeviceCommunicationPageViewModelResumeTests
{
    [TestMethod]
    public void RefreshCurrentConversationView_NotifiesMessageBindings()
    {
        using var viewModel = new DeviceCommunicationPageViewModel(
            new FakeDiscoveryService(),
            new FakeMessageAppService(),
            new FakeChatAttachmentStore(),
            new FakeChatPlatformService(),
            new FakeDeviceCommunicationSettings(),
            new FakeToastService(),
            postToUi: action => action());
        viewModel.SelectedConversation = new DeviceConversationItem("peer-1");
        var properties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => properties.Add(args.PropertyName);

        viewModel.RefreshCurrentConversationView();

        CollectionAssert.Contains(properties, nameof(DeviceCommunicationPageViewModel.CurrentMessages));
        CollectionAssert.Contains(properties, nameof(DeviceCommunicationPageViewModel.MessageListVersion));
    }

    [TestMethod]
    public void RefreshCurrentConversationView_RequestsMessageVisualRebuild()
    {
        using var viewModel = new DeviceCommunicationPageViewModel(
            new FakeDiscoveryService(),
            new FakeMessageAppService(),
            new FakeChatAttachmentStore(),
            new FakeChatPlatformService(),
            new FakeDeviceCommunicationSettings(),
            new FakeToastService(),
            postToUi: action => action());
        viewModel.SelectedConversation = new DeviceConversationItem("peer-1");
        var initialVersion = viewModel.MessageViewRefreshVersion;
        var properties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => properties.Add(args.PropertyName);

        viewModel.RefreshCurrentConversationView();

        Assert.AreEqual(initialVersion + 1, viewModel.MessageViewRefreshVersion);
        CollectionAssert.Contains(properties, nameof(DeviceCommunicationPageViewModel.MessageViewRefreshVersion));
    }

    [TestMethod]
    public async Task MainViewModel_StartAsync_RefreshesCurrentConversationView()
    {
        using var chat = new DeviceCommunicationPageViewModel(
            new FakeDiscoveryService(),
            new FakeMessageAppService(),
            new FakeChatAttachmentStore(),
            new FakeChatPlatformService(),
            new FakeDeviceCommunicationSettings(),
            new FakeToastService(),
            postToUi: action => action());
        chat.SelectedConversation = new DeviceConversationItem("peer-1");
        var host = new MobileDeviceCommunicationHost(
            new FakeCommunicationRuntime(),
            new FakeDiscoveryService());
        var viewModel = new MainViewModel(chat, host);
        var properties = new List<string?>();
        chat.PropertyChanged += (_, args) => properties.Add(args.PropertyName);

        await viewModel.StartAsync();

        CollectionAssert.Contains(properties, nameof(DeviceCommunicationPageViewModel.CurrentMessages));
    }

    [TestMethod]
    public async Task IncomingMessage_ForBackgroundConversation_ShowsNotification()
    {
        var notificationService = new FakeToastService();
        var incomingEvent = new ChatMessageReceivedEvent(
            new TextChatMessage("peer-1", "hello"),
            null,
            "peer-1",
            DateTimeOffset.UtcNow);
        using var viewModel = new DeviceCommunicationPageViewModel(
            new FakeDiscoveryService(new DiscoveredDevice { Id = "peer-1", Name = "Phone" }),
            new FakeMessageAppService(incomingEvent, IncomingMessageDisplayMode.NotifyByToast),
            new FakeChatAttachmentStore(),
            new FakeChatPlatformService(),
            new FakeDeviceCommunicationSettings(),
            notificationService,
            autoSelectFirstConversation: false,
            postToUi: action => action());

        var notification = await notificationService.NotificationShown.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual("Phone", notification.Header);
        Assert.AreEqual("hello", notification.Text);
        Assert.AreEqual(1, viewModel.Conversations.Single().UnreadCount);
    }

    [TestMethod]
    public void IncomingMessage_WhenRuntimeHandlesNotifications_DoesNotShowDuplicate()
    {
        var notificationService = new FakeToastService(incomingMessagesHandledExternally: true);
        var incomingEvent = new ChatMessageReceivedEvent(
            new TextChatMessage("peer-1", "hello"),
            null,
            "peer-1",
            DateTimeOffset.UtcNow);
        using var viewModel = new DeviceCommunicationPageViewModel(
            new FakeDiscoveryService(new DiscoveredDevice { Id = "peer-1", Name = "Phone" }),
            new FakeMessageAppService(incomingEvent, IncomingMessageDisplayMode.NotifyByToast),
            new FakeChatAttachmentStore(),
            new FakeChatPlatformService(),
            new FakeDeviceCommunicationSettings(),
            notificationService,
            autoSelectFirstConversation: false,
            postToUi: action => action());

        Assert.AreEqual(1, viewModel.Conversations.Single().UnreadCount);
        Assert.AreEqual(0, notificationService.ShowCount);
    }

    [TestMethod]
    public async Task SendFiles_DroppedLocalFile_SendsToSelectedConversation()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        var expectedContent = "dragged file content";
        await File.WriteAllTextAsync(filePath, expectedContent);

        try
        {
            var messageService = new FakeMessageAppService();
            using var viewModel = new DeviceCommunicationPageViewModel(
                new FakeDiscoveryService(new DiscoveredDevice { Id = "peer-1", Name = "Phone" }),
                messageService,
                new FakeChatAttachmentStore(),
                new FakeChatPlatformService(),
                new FakeDeviceCommunicationSettings(),
                new FakeToastService(),
                postToUi: action => action());
            viewModel.SelectedConversation = viewModel.Conversations.Single();

            viewModel.SendFiles([filePath]);

            var sentFile = await messageService.FileSent.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.AreEqual("peer-1", sentFile.DeviceId);
            Assert.AreEqual(Path.GetFileName(filePath), sentFile.Message.FileName);
            Assert.AreEqual(expectedContent, System.Text.Encoding.UTF8.GetString(sentFile.Content));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private sealed class FakeCommunicationRuntime : IMobileCommunicationRuntime
    {
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
    }

    private sealed class FakeDiscoveryService : IDeviceDiscoveryService
    {
        private readonly ObservableList<DiscoveredDevice> _source = [];
        private readonly ISynchronizedView<DiscoveredDevice, DiscoveredDevice> _view;

        public FakeDiscoveryService(params DiscoveredDevice[] devices)
        {
            foreach (var device in devices)
            {
                _source.Add(device);
            }

            _view = _source.CreateView(device => device);
            Devices = _view.ToNotifyCollectionChanged();
        }

        public NotifyCollectionChangedSynchronizedViewList<DiscoveredDevice> Devices { get; }
        public Task StartAsync(CancellationToken token) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;

        public void Dispose()
        {
            Devices.Dispose();
            _view.Dispose();
        }
    }

    private sealed class FakeMessageAppService : IMessageAppService
    {
        private readonly DeviceMessageEvent? _incomingEvent;
        private readonly IncomingMessageDisplayMode _displayMode;

        public FakeMessageAppService(
            DeviceMessageEvent? incomingEvent = null,
            IncomingMessageDisplayMode displayMode = IncomingMessageDisplayMode.ShowInCurrentConversation)
        {
            _incomingEvent = incomingEvent;
            _displayMode = displayMode;
        }

        public TaskCompletionSource<(string DeviceId, FileChatMessage Message, byte[] Content)> FileSent { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask SendTextChatAsync(string deviceId, string text, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public async ValueTask SendFileChatAsync(string deviceId, FileChatMessage message, Stream stream, CancellationToken cancellationToken = default)
        {
            await using var content = new MemoryStream();
            await stream.CopyToAsync(content, cancellationToken);
            FileSent.TrySetResult((deviceId, message, content.ToArray()));
        }
        public ValueTask SendImageChatAsync(string deviceId, ImageChatMessage message, Stream stream, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask AcceptFileAsync(string deviceId, Guid transferId, string savePath, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask AcceptFileAsync(string deviceId, Guid transferId, string saveTarget, Func<CancellationToken, ValueTask<Stream>> openWriteStreamAsync, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RejectFileAsync(string deviceId, Guid transferId, string reason, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask CancelTransferAsync(string deviceId, Guid transferId, string reason, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask SendClipboardTextAsync(string deviceId, TextClipboardMessage message, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public async IAsyncEnumerable<DeviceMessageEvent> ReceiveAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (_incomingEvent is not null)
            {
                yield return _incomingEvent;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield break;
        }

        public void UpdateDisplayContext(bool isMainWindowActive, bool isDeviceChatPageOpen, string? selectedConversationId) { }
        public void RequestOpenConversation(string conversationId) { }
        public string? GetRequestedConversationId() => null;
        public void ClearRequestedConversationId() { }
        public IncomingMessageDisplayMode ResolveIncomingDisplayMode(string conversationId) => _displayMode;
        public IncomingMessageDisplayMode ResolveIncomingDisplayMode(bool isMainWindowActive, bool isDeviceChatPageOpen, string conversationId, string? selectedConversationId) => _displayMode;
    }

    private sealed class FakeChatPlatformService : IChatPlatformService
    {
        public bool CanOpenFile => false;
        public void OpenFile(string path) { }
        public Task<string?> PromptTextAsync(string title, string prompt, string? initialValue) => Task.FromResult<string?>(null);
        public ChatDisplayContext GetDisplayContext(string? selectedConversationId) => new(true, true);
    }

    private sealed class FakeChatAttachmentStore : IChatAttachmentStore
    {
        public Task<IReadOnlyList<string>> PickFilesToSendAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        public Task<ChatFileSaveTarget?> PickSaveTargetAsync(
            string suggestedFileName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ChatFileSaveTarget?>(null);
        }
    }

    private sealed class FakeDeviceCommunicationSettings : Kitopia.Feature.DeviceCommunication.Discovery.IDeviceCommunicationSettings
    {
        public string BroadcastName => "Fake";
        public string? GetCustomName(string publicKey) => null;
        public void SetCustomName(string publicKey, string name) { }
        public void RemoveCustomName(string publicKey) { }
    }

    private sealed class FakeToastService : IChatNotificationSink
    {
        public FakeToastService(bool incomingMessagesHandledExternally = false)
        {
            IncomingMessagesHandledExternally = incomingMessagesHandledExternally;
        }

        public TaskCompletionSource<(string Header, string Text)> NotificationShown { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IncomingMessagesHandledExternally { get; }
        public int ShowCount { get; private set; }

        public Task ShowAsync(
            string header,
            string text,
            ChatNotificationKind kind = ChatNotificationKind.Information,
            bool persistent = false)
        {
            ShowCount++;
            NotificationShown.TrySetResult((header, text));
            return Task.CompletedTask;
        }
    }
}
