using System.Collections.ObjectModel;
using System.Threading.Channels;
using Kitopia.DeviceCommunication.Application;
using Kitopia.DeviceCommunication.Discovery;
using Kitopia.DeviceCommunication.Messages.Chat;
using Kitopia.DeviceCommunication.Messages.Clipboard;
using Kitopia.Mobile.Services;
using Kitopia.Mobile.ViewModels;

namespace KitopiaTest.Mobile;

[TestClass]
public sealed class MobileConversationViewModelTests
{
    [TestMethod]
    public async Task DeviceAppearance_AndSelection_UpdatesConversationTarget()
    {
        var discovery = new FakeDiscoveryService();
        var messageService = new FakeMessageAppService();
        await using var viewModel = CreateMainViewModel(discovery, messageService);
        await viewModel.StartAsync();

        var device = new DiscoveredDevice { Id = "peer-1", Name = "Phone", TcpPort = 22001 };
        discovery.Devices.Add(device);
        viewModel.DeviceList.SelectedDevice = device;

        Assert.AreEqual(1, viewModel.DeviceList.Devices.Count);
        Assert.AreEqual("peer-1", viewModel.Conversation.SelectedConversationId);
        Assert.IsTrue(viewModel.IsConversationOpen);
    }

    [TestMethod]
    public async Task SendTextAsync_AddsPendingMessage_ThenMarksSuccessful()
    {
        var discovery = new FakeDiscoveryService();
        var messageService = new FakeMessageAppService();
        await using var viewModel = CreateMainViewModel(discovery, messageService);
        await viewModel.StartAsync();
        var device = new DiscoveredDevice { Id = "peer-1", Name = "Phone" };
        discovery.Devices.Add(device);
        viewModel.DeviceList.SelectedDevice = device;
        viewModel.Conversation.DraftText = "hello";

        await viewModel.Conversation.SendTextCommand.ExecuteAsync(null);

        Assert.AreEqual(1, viewModel.Conversation.Messages.Count);
        Assert.AreEqual("hello", viewModel.Conversation.Messages[0].Text);
        Assert.IsFalse(viewModel.Conversation.Messages[0].IsPending);
        Assert.IsFalse(viewModel.Conversation.Messages[0].IsFailed);
        CollectionAssert.AreEqual(new[] { "hello" }, messageService.SentTexts);
    }

    [TestMethod]
    public async Task SendTextAsync_WhenSendFails_MarksMessageFailed()
    {
        var discovery = new FakeDiscoveryService();
        var messageService = new FakeMessageAppService { SendTextException = new InvalidOperationException("boom") };
        await using var viewModel = CreateMainViewModel(discovery, messageService);
        await viewModel.StartAsync();
        var device = new DiscoveredDevice { Id = "peer-1", Name = "Phone" };
        discovery.Devices.Add(device);
        viewModel.DeviceList.SelectedDevice = device;
        viewModel.Conversation.DraftText = "hello";

        await viewModel.Conversation.SendTextCommand.ExecuteAsync(null);

        Assert.AreEqual(1, viewModel.Conversation.Messages.Count);
        Assert.IsTrue(viewModel.Conversation.Messages[0].IsFailed);
        StringAssert.Contains(viewModel.Conversation.Messages[0].Reason, "boom");
    }

    [TestMethod]
    public async Task SendClipboardAsync_SendsCurrentClipboardText()
    {
        var discovery = new FakeDiscoveryService();
        var messageService = new FakeMessageAppService();
        var clipboard = new FakeClipboardService("clip-text");
        await using var viewModel = CreateMainViewModel(discovery, messageService, clipboardService: clipboard);
        await viewModel.StartAsync();
        var device = new DiscoveredDevice { Id = "peer-1", Name = "Phone" };
        discovery.Devices.Add(device);
        viewModel.DeviceList.SelectedDevice = device;

        await viewModel.Conversation.SendClipboardCommand.ExecuteAsync(null);

        Assert.AreEqual(1, messageService.SentClipboardTexts.Count);
        Assert.AreEqual("clip-text", messageService.SentClipboardTexts[0]);
        Assert.AreEqual("[Clipboard] clip-text", viewModel.Conversation.Messages[0].Text);
    }

    [TestMethod]
    public async Task SendFileAsync_UsesPickedFile()
    {
        var discovery = new FakeDiscoveryService();
        var messageService = new FakeMessageAppService();
        await using var pickedFile = await FakePickedFileAsync("note.txt", "payload");
        var filePicker = new FakeFilePickerService(pickedFile: pickedFile);
        await using var viewModel = CreateMainViewModel(discovery, messageService, filePicker);
        await viewModel.StartAsync();
        var device = new DiscoveredDevice { Id = "peer-1", Name = "Phone" };
        discovery.Devices.Add(device);
        viewModel.DeviceList.SelectedDevice = device;

        await viewModel.Conversation.SendFileCommand.ExecuteAsync(null);

        Assert.AreEqual(1, messageService.SentFiles.Count);
        Assert.AreEqual("note.txt", messageService.SentFiles[0].FileName);
        Assert.AreEqual("[File] note.txt", viewModel.Conversation.Messages[0].Text);
    }

    [TestMethod]
    public async Task IncomingTextEvent_AddsIncomingMessage()
    {
        var discovery = new FakeDiscoveryService();
        var messageService = new FakeMessageAppService();
        await using var viewModel = CreateMainViewModel(discovery, messageService);
        await viewModel.StartAsync();
        var device = new DiscoveredDevice { Id = "peer-1", Name = "Phone" };
        discovery.Devices.Add(device);
        viewModel.DeviceList.SelectedDevice = device;

        await messageService.PublishAsync(new ChatMessageReceivedEvent(
            new TextChatMessage("peer-1", "hi"),
            null,
            "peer-1",
            DateTimeOffset.UtcNow));

        await WaitForAsync(() => viewModel.Conversation.Messages.Count == 1);
        Assert.AreEqual("hi", viewModel.Conversation.Messages[0].Text);
        Assert.IsFalse(viewModel.Conversation.Messages[0].IsOutgoing);
    }

    [TestMethod]
    public async Task IncomingClipboardEvent_CopiesToLocalClipboard()
    {
        var discovery = new FakeDiscoveryService();
        var messageService = new FakeMessageAppService();
        var clipboard = new FakeClipboardService();
        await using var viewModel = CreateMainViewModel(discovery, messageService, clipboardService: clipboard);
        await viewModel.StartAsync();
        var device = new DiscoveredDevice { Id = "peer-1", Name = "Phone" };
        discovery.Devices.Add(device);
        viewModel.DeviceList.SelectedDevice = device;

        await messageService.PublishAsync(new ChatMessageReceivedEvent(
            new TextClipboardMessage("peer-1", "from-remote"),
            null,
            "peer-1",
            DateTimeOffset.UtcNow));

        await WaitForAsync(() => viewModel.Conversation.Messages.Count == 1);
        Assert.AreEqual("from-remote", clipboard.LastSetText);
        Assert.AreEqual("[Clipboard] from-remote", viewModel.Conversation.Messages[0].Text);
    }

    [TestMethod]
    public async Task IncomingFileOffer_AllowsAcceptAction()
    {
        var discovery = new FakeDiscoveryService();
        var messageService = new FakeMessageAppService();
        var filePicker = new FakeFilePickerService(savePath: @"C:\tmp\sample.bin");
        await using var viewModel = CreateMainViewModel(discovery, messageService, filePicker);
        await viewModel.StartAsync();
        var device = new DiscoveredDevice { Id = "peer-1", Name = "Phone" };
        discovery.Devices.Add(device);
        viewModel.DeviceList.SelectedDevice = device;
        var transferId = Guid.NewGuid();

        await messageService.PublishAsync(new FileTransferUpdatedEvent(
            "peer-1",
            transferId,
            FileTransferDirection.Download,
            FileTransferStatus.WaitingForAccept,
            "sample.bin",
            null,
            1024,
            null,
            DateTimeOffset.UtcNow));

        await WaitForAsync(() => viewModel.Conversation.Messages.Count == 1);
        var item = viewModel.Conversation.Messages[0];
        Assert.IsTrue(item.CanHandleIncomingOffer);

        await viewModel.Conversation.AcceptIncomingOfferCommand.ExecuteAsync(item);

        Assert.AreEqual(1, messageService.AcceptedTransfers.Count);
        Assert.AreEqual(transferId, messageService.AcceptedTransfers[0].TransferId);
        Assert.IsTrue(item.IsHandled);
    }

    [TestMethod]
    public async Task IncomingFileOffer_AllowsRejectAction()
    {
        var discovery = new FakeDiscoveryService();
        var messageService = new FakeMessageAppService();
        await using var viewModel = CreateMainViewModel(discovery, messageService);
        await viewModel.StartAsync();
        var device = new DiscoveredDevice { Id = "peer-1", Name = "Phone" };
        discovery.Devices.Add(device);
        viewModel.DeviceList.SelectedDevice = device;
        var transferId = Guid.NewGuid();

        await messageService.PublishAsync(new FileTransferUpdatedEvent(
            "peer-1",
            transferId,
            FileTransferDirection.Download,
            FileTransferStatus.WaitingForAccept,
            "sample.bin",
            null,
            1024,
            null,
            DateTimeOffset.UtcNow));

        await WaitForAsync(() => viewModel.Conversation.Messages.Count == 1);
        var item = viewModel.Conversation.Messages[0];

        await viewModel.Conversation.RejectIncomingOfferCommand.ExecuteAsync(item);

        Assert.AreEqual(1, messageService.RejectedTransfers.Count);
        Assert.AreEqual(transferId, messageService.RejectedTransfers[0].TransferId);
        Assert.IsTrue(item.IsHandled);
    }

    [TestMethod]
    public async Task CancelTransferCommand_CancelsActiveTransfer()
    {
        var discovery = new FakeDiscoveryService();
        var messageService = new FakeMessageAppService();
        await using var viewModel = CreateMainViewModel(discovery, messageService);
        await viewModel.StartAsync();
        var device = new DiscoveredDevice { Id = "peer-1", Name = "Phone" };
        discovery.Devices.Add(device);
        viewModel.DeviceList.SelectedDevice = device;
        var transferId = Guid.NewGuid();

        await messageService.PublishAsync(new FileTransferUpdatedEvent(
            "peer-1",
            transferId,
            FileTransferDirection.Upload,
            FileTransferStatus.InProgress,
            "sample.bin",
            20,
            100,
            null,
            DateTimeOffset.UtcNow));

        await WaitForAsync(() => viewModel.Conversation.Messages.Count == 1);
        var item = viewModel.Conversation.Messages[0];
        Assert.IsTrue(item.CanCancelTransfer);

        await viewModel.Conversation.CancelTransferCommand.ExecuteAsync(item);

        Assert.AreEqual(1, messageService.CancelledTransfers.Count);
        Assert.AreEqual(transferId, messageService.CancelledTransfers[0].TransferId);
        Assert.IsTrue(item.IsFailed);
    }

    [TestMethod]
    public async Task TransferProgress_ThenComplete_UpdatesExistingMessage()
    {
        var discovery = new FakeDiscoveryService();
        var messageService = new FakeMessageAppService();
        await using var viewModel = CreateMainViewModel(discovery, messageService);
        await viewModel.StartAsync();
        var device = new DiscoveredDevice { Id = "peer-1", Name = "Phone" };
        discovery.Devices.Add(device);
        viewModel.DeviceList.SelectedDevice = device;
        var transferId = Guid.NewGuid();

        await messageService.PublishAsync(new FileTransferUpdatedEvent("peer-1", transferId, FileTransferDirection.Download, FileTransferStatus.WaitingForAccept, "sample.bin", null, 100, null, DateTimeOffset.UtcNow));
        await WaitForAsync(() => viewModel.Conversation.Messages.Count == 1);
        await messageService.PublishAsync(new FileTransferUpdatedEvent("peer-1", transferId, FileTransferDirection.Download, FileTransferStatus.InProgress, "sample.bin", 50, 100, null, DateTimeOffset.UtcNow));
        await WaitForAsync(() => viewModel.Conversation.Messages[0].IsReceiving);
        await messageService.PublishAsync(new FileTransferUpdatedEvent("peer-1", transferId, FileTransferDirection.Download, FileTransferStatus.Completed, "sample.bin", 100, 100, null, DateTimeOffset.UtcNow));
        await WaitForAsync(() => !viewModel.Conversation.Messages[0].IsReceiving);

        Assert.AreEqual(100d, viewModel.Conversation.Messages[0].ProgressPercent, 0.1d);
        Assert.IsFalse(viewModel.Conversation.Messages[0].IsFailed);
        Assert.IsTrue(viewModel.Conversation.Messages[0].IsHandled);
    }

    [TestMethod]
    public async Task TransferFailed_UpdatesExistingMessageAsFailed()
    {
        var discovery = new FakeDiscoveryService();
        var messageService = new FakeMessageAppService();
        await using var viewModel = CreateMainViewModel(discovery, messageService);
        await viewModel.StartAsync();
        var device = new DiscoveredDevice { Id = "peer-1", Name = "Phone" };
        discovery.Devices.Add(device);
        viewModel.DeviceList.SelectedDevice = device;
        var transferId = Guid.NewGuid();

        await messageService.PublishAsync(new FileTransferUpdatedEvent("peer-1", transferId, FileTransferDirection.Download, FileTransferStatus.WaitingForAccept, "sample.bin", null, 100, null, DateTimeOffset.UtcNow));
        await WaitForAsync(() => viewModel.Conversation.Messages.Count == 1);
        await messageService.PublishAsync(new FileTransferUpdatedEvent("peer-1", transferId, FileTransferDirection.Download, FileTransferStatus.Failed, "sample.bin", 20, 100, "disk_full", DateTimeOffset.UtcNow));
        await WaitForAsync(() => viewModel.Conversation.Messages[0].IsFailed);

        Assert.IsTrue(viewModel.Conversation.Messages[0].IsFailed);
        StringAssert.Contains(viewModel.Conversation.Messages[0].Reason, "disk_full");
    }

    private static async Task WaitForAsync(Func<bool> predicate, int timeoutMs = 2000)
    {
        var started = Environment.TickCount64;
        while (!predicate())
        {
            if (Environment.TickCount64 - started > timeoutMs)
            {
                Assert.Fail("Condition was not satisfied in time.");
            }

            await Task.Delay(20);
        }
    }

    private static async Task<MobilePickedFile> FakePickedFileAsync(string name, string content)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"kitopia-mobile-test-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tempPath, content);
        return new MobilePickedFile(name, "text/plain", new FileInfo(tempPath).Length, tempPath);
    }

    private static MainViewModel CreateMainViewModel(
        FakeDiscoveryService discovery,
        FakeMessageAppService messageService,
        IMobileFilePickerService? filePicker = null,
        IMobileClipboardService? clipboardService = null)
    {
        return new MainViewModel(
            new DeviceListViewModel(discovery),
            new ConversationViewModel(
                messageService,
                filePicker ?? new NullMobileFilePickerService(),
                clipboardService ?? new FakeClipboardService()),
            new MobileDeviceCommunicationHost(new FakeCommunicationRuntime(), discovery));
    }

    private sealed class FakeDiscoveryService : IDeviceDiscoveryService
    {
        public ObservableCollection<DiscoveredDevice> Devices { get; } = [];
        public Task StartAsync(CancellationToken token) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class FakeCommunicationRuntime : IMobileCommunicationRuntime
    {
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
    }

    private sealed class FakeClipboardService : IMobileClipboardService
    {
        private string? _text;

        public FakeClipboardService(string? initialText = null)
        {
            _text = initialText;
        }

        public string? LastSetText { get; private set; }

        public Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(_text);
        }

        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            _text = text;
            LastSetText = text;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFilePickerService : IMobileFilePickerService
    {
        private readonly string? _savePath;
        private readonly MobilePickedFile? _pickedFile;

        public FakeFilePickerService(string? savePath = null, MobilePickedFile? pickedFile = null)
        {
            _savePath = savePath;
            _pickedFile = pickedFile;
        }

        public Task<string?> PickSavePathAsync(string suggestedFileName, CancellationToken cancellationToken = default)
        {
            _ = suggestedFileName;
            _ = cancellationToken;
            return Task.FromResult(_savePath);
        }

        public Task<MobilePickedFile?> PickFileToSendAsync(MobilePickedFileKind kind, CancellationToken cancellationToken = default)
        {
            _ = kind;
            _ = cancellationToken;
            return Task.FromResult(_pickedFile);
        }
    }

    private sealed class FakeMessageAppService : IMessageAppService
    {
        private readonly Channel<DeviceMessageEvent> _channel = Channel.CreateUnbounded<DeviceMessageEvent>();

        public List<string> SentTexts { get; } = [];
        public List<string> SentClipboardTexts { get; } = [];
        public List<(string FileName, long? Length)> SentFiles { get; } = [];
        public List<(string ContentType, long SizeBytes)> SentImages { get; } = [];
        public List<(string DeviceId, Guid TransferId, string SavePath)> AcceptedTransfers { get; } = [];
        public List<(string DeviceId, Guid TransferId, string Reason)> RejectedTransfers { get; } = [];
        public List<(string DeviceId, Guid TransferId, string Reason)> CancelledTransfers { get; } = [];
        public Exception? SendTextException { get; set; }

        public async Task PublishAsync(DeviceMessageEvent messageEvent)
        {
            await _channel.Writer.WriteAsync(messageEvent);
        }

        public ValueTask SendTextChatAsync(string deviceId, string text, CancellationToken cancellationToken = default)
        {
            if (SendTextException is not null)
            {
                throw SendTextException;
            }

            _ = deviceId;
            _ = cancellationToken;
            SentTexts.Add(text);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendFileChatAsync(string deviceId, FileChatMessage message, Stream stream, CancellationToken cancellationToken = default)
        {
            _ = deviceId;
            _ = stream;
            _ = cancellationToken;
            SentFiles.Add((message.FileName, message.Length));
            return ValueTask.CompletedTask;
        }

        public ValueTask SendImageChatAsync(string deviceId, ImageChatMessage message, Stream stream, CancellationToken cancellationToken = default)
        {
            _ = deviceId;
            _ = stream;
            _ = cancellationToken;
            SentImages.Add((message.ContentType, message.SizeBytes));
            return ValueTask.CompletedTask;
        }

        public ValueTask AcceptFileAsync(string deviceId, Guid transferId, string savePath, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            AcceptedTransfers.Add((deviceId, transferId, savePath));
            return ValueTask.CompletedTask;
        }

        public ValueTask RejectFileAsync(string deviceId, Guid transferId, string reason, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            RejectedTransfers.Add((deviceId, transferId, reason));
            return ValueTask.CompletedTask;
        }

        public ValueTask CancelTransferAsync(string deviceId, Guid transferId, string reason, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            CancelledTransfers.Add((deviceId, transferId, reason));
            return ValueTask.CompletedTask;
        }

        public ValueTask SendClipboardTextAsync(string deviceId, TextClipboardMessage message, CancellationToken cancellationToken = default)
        {
            _ = deviceId;
            _ = cancellationToken;
            SentClipboardTexts.Add(message.Text);
            return ValueTask.CompletedTask;
        }

        public IAsyncEnumerable<DeviceMessageEvent> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            return _channel.Reader.ReadAllAsync(cancellationToken);
        }

        public void UpdateDisplayContext(bool isMainWindowActive, bool isDeviceChatPageOpen, string? selectedConversationId)
        {
            _ = isMainWindowActive;
            _ = isDeviceChatPageOpen;
            _ = selectedConversationId;
        }

        public void RequestOpenConversation(string conversationId) => _ = conversationId;
        public string? GetRequestedConversationId() => null;
        public void ClearRequestedConversationId() { }
        public IncomingMessageDisplayMode ResolveIncomingDisplayMode(string conversationId) => IncomingMessageDisplayMode.ShowInCurrentConversation;
        public IncomingMessageDisplayMode ResolveIncomingDisplayMode(bool isMainWindowActive, bool isDeviceChatPageOpen, string conversationId, string? selectedConversationId) => IncomingMessageDisplayMode.ShowInCurrentConversation;
    }
}
