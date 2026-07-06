using Avalonia.Controls.Notifications;
using Core.Services.DeviceCommunication.Application;
using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Messages.Clipboard;
using Core.Services.DeviceCommunication.Platform;
using Core.Services.Interfaces;
using Core.ViewModel.Pages.device;
using Kitopia.DeviceCommunication.Discovery;
using Kitopia.Mobile.Services;
using Kitopia.Mobile.ViewModels;
using ObservableCollections;
using PluginCore;

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
            new FakeChatPlatformService(),
            new FakeDeviceCommunicationSettings(),
            new FakeToastService());
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
            new FakeChatPlatformService(),
            new FakeDeviceCommunicationSettings(),
            new FakeToastService());
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
            new FakeChatPlatformService(),
            new FakeDeviceCommunicationSettings(),
            new FakeToastService());
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

    private sealed class FakeCommunicationRuntime : IMobileCommunicationRuntime
    {
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
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
        public ValueTask SendTextChatAsync(string deviceId, string text, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask SendFileChatAsync(string deviceId, FileChatMessage message, Stream stream, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask SendImageChatAsync(string deviceId, ImageChatMessage message, Stream stream, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask AcceptFileAsync(string deviceId, Guid transferId, string savePath, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask AcceptFileAsync(string deviceId, Guid transferId, string saveTarget, Func<CancellationToken, ValueTask<Stream>> openWriteStreamAsync, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask RejectFileAsync(string deviceId, Guid transferId, string reason, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask CancelTransferAsync(string deviceId, Guid transferId, string reason, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask SendClipboardTextAsync(string deviceId, TextClipboardMessage message, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public async IAsyncEnumerable<DeviceMessageEvent> ReceiveAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield break;
        }

        public void UpdateDisplayContext(bool isMainWindowActive, bool isDeviceChatPageOpen, string? selectedConversationId) { }
        public void RequestOpenConversation(string conversationId) { }
        public string? GetRequestedConversationId() => null;
        public void ClearRequestedConversationId() { }
        public IncomingMessageDisplayMode ResolveIncomingDisplayMode(string conversationId) => IncomingMessageDisplayMode.ShowInCurrentConversation;
        public IncomingMessageDisplayMode ResolveIncomingDisplayMode(bool isMainWindowActive, bool isDeviceChatPageOpen, string conversationId, string? selectedConversationId) => IncomingMessageDisplayMode.ShowInCurrentConversation;
    }

    private sealed class FakeChatPlatformService : IChatPlatformService
    {
        public Task<IReadOnlyList<string>> PickFilesToSendAsync() => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<ChatFileSaveTarget?> PickSaveTargetAsync(string suggestedFileName) => Task.FromResult<ChatFileSaveTarget?>(null);
        public bool CanOpenFile => false;
        public void OpenFile(string path) { }
        public Task CopyTextToClipboardAsync(string text) => Task.CompletedTask;
        public Task<string?> PromptTextAsync(string title, string prompt, string? initialValue) => Task.FromResult<string?>(null);
        public ChatDisplayContext GetDisplayContext(string? selectedConversationId) => new(true, true);
    }

    private sealed class FakeDeviceCommunicationSettings : Kitopia.DeviceCommunication.Discovery.IDeviceCommunicationSettings
    {
        public string BroadcastName => "Fake";
        public string? GetCustomName(string publicKey) => null;
        public void SetCustomName(string publicKey, string name) { }
        public void RemoveCustomName(string publicKey) { }
    }

    private sealed class FakeToastService : IToastService
    {
        public void Init() { }
        public Task Show(string header, string text, NotificationType notificationType = NotificationType.Information, Avalonia.Controls.Window? dialogWindow = null) => Task.CompletedTask;
        public Task Show(ToastRequest request, Avalonia.Controls.Window? dialogWindow = null) => Task.CompletedTask;
        public IToastProgressHandle ShowProgress(string header, string text, NotificationType notificationType, double initialProgress = 0, bool isIndeterminate = false) => throw new NotSupportedException();
        public bool HasUnreadSuppressedNotifications() => false;
        public bool TryOpenLatestSuppressedNotification() => false;
        public bool ShowSuppressedNotificationCenter() => false;
        public void ClearUnreadSuppressedNotifications() { }
        public void Unregister() { }
    }
}
