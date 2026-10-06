using System.Threading.Channels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kitopia.Feature.DeviceCommunication.Application;
using Kitopia.Feature.Avalonia.DeviceCommunication.ViewModels;
using Kitopia.Feature.Avalonia.DeviceCommunication.Views;
using Kitopia.Feature.DeviceCommunication.Discovery;
using Kitopia.Feature.DeviceCommunication.Messages.Chat;
using Kitopia.Feature.Localization;
using Kitopia.Mobile.Services;
using Kitopia.Mobile.ViewModels;
using ObservableCollections;

namespace KitopiaTest.Mobile;

[TestClass]
[DoNotParallelize]
public sealed class DeviceCommunicationPageViewModelResumeTests
{
    public TestContext TestContext { get; set; } = null!;

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Kitopia.Desktop.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

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

    [TestMethod]
    public async Task IncomingFile_RejectCommand_ShowsRejectedAndDisablesReceiveActions()
    {
        using var discovery = new FakeDiscoveryService(new DiscoveredDevice { Id = "peer-1", Name = "Phone" });
        var offer = FileEvent(Guid.NewGuid(), FileTransferStatus.WaitingForAccept);
        using var viewModel = new DeviceCommunicationPageViewModel(discovery, new FakeMessageAppService(offer),
            new FakeChatAttachmentStore(), new FakeChatPlatformService(), new FakeDeviceCommunicationSettings(),
            new FakeToastService(), postToUi: action => action());
        var item = viewModel.Conversations.Single().Messages.OfType<FileChatMessageItem>().Single();

        await viewModel.RejectIncomingOfferCommand.ExecuteAsync(item);

        Assert.AreEqual(FileTransferStatus.Rejected, item.Status);
        Assert.AreEqual(Lang.Get("lang.kitopia.rejected"), item.StateText);
        Assert.IsFalse(item.CanHandleIncomingOffer);
        Assert.IsFalse(item.IsTransferActive);
        Assert.IsFalse(item.CanUseLocalFile);
        StringAssert.Contains(viewModel.Conversations.Single().LastMessagePreview, item.StateText);
    }

    [TestMethod]
    [DataRow(FileTransferStatus.Rejected, "lang.kitopia.rejected")]
    [DataRow(FileTransferStatus.Cancelled, "lang.kitopia.cancelled")]
    [DataRow(FileTransferStatus.Timeout, "lang.kitopia.transfer_timed_out")]
    [DataRow(FileTransferStatus.Failed, "lang.kitopia.failed")]
    public void IncomingFile_TerminalEventThenLateProgress_PreservesTerminalState(FileTransferStatus status, string key)
    {
        using var discovery = new FakeDiscoveryService(new DiscoveredDevice { Id = "peer-1", Name = "Phone" });
        var transferId = Guid.NewGuid();
        var events = new[]
        {
            FileEvent(transferId, FileTransferStatus.WaitingForAccept),
            FileEvent(transferId, FileTransferStatus.InProgress),
            FileEvent(transferId, status),
            FileEvent(transferId, FileTransferStatus.Accepted),
            FileEvent(transferId, FileTransferStatus.InProgress),
            FileEvent(transferId, FileTransferStatus.Completed)
        };
        using var viewModel = new DeviceCommunicationPageViewModel(discovery, new FakeMessageAppService(incomingEvents: events),
            new FakeChatAttachmentStore(), new FakeChatPlatformService(), new FakeDeviceCommunicationSettings(),
            new FakeToastService(), postToUi: action => action());
        var item = viewModel.Conversations.Single().Messages.OfType<FileChatMessageItem>().Single();

        Assert.AreEqual(status, item.Status);
        Assert.AreEqual(Lang.Get(key), item.StateText);
        Assert.IsFalse(item.IsReceiving);
        Assert.IsFalse(item.CanHandleIncomingOffer);
        Assert.IsFalse(item.IsTransferActive);
        Assert.IsFalse(item.CanUseLocalFile);
        Assert.AreEqual(0d, item.TransferSpeedBytesPerSecond);
        StringAssert.Contains(viewModel.Conversations.Single().LastMessagePreview, item.StateText);
    }

    [TestMethod]
    public void IncomingFile_CompletedBeforeLateAcceptance_KeepsSavedPathAndOneCard()
    {
        using var discovery = new FakeDiscoveryService(new DiscoveredDevice { Id = "peer-1", Name = "Phone" });
        var transferId = Guid.NewGuid();
        var path = Path.GetTempFileName();
        try
        {
            var events = new[]
            {
                FileEvent(transferId, FileTransferStatus.WaitingForAccept),
                FileEvent(transferId, FileTransferStatus.InProgress),
                FileEvent(transferId, FileTransferStatus.Completed) with { LocalFilePath = path },
                FileEvent(transferId, FileTransferStatus.Accepted),
                FileEvent(transferId, FileTransferStatus.Delivered),
                FileEvent(transferId, FileTransferStatus.InProgress),
                FileEvent(transferId, FileTransferStatus.WaitingForAccept)
            };
            using var viewModel = new DeviceCommunicationPageViewModel(discovery, new FakeMessageAppService(incomingEvents: events),
                new FakeChatAttachmentStore(), new FakeChatPlatformService(), new FakeDeviceCommunicationSettings(),
                new FakeToastService(), postToUi: action => action());
            var item = viewModel.Conversations.Single().Messages.OfType<FileChatMessageItem>().Single();
            Assert.AreEqual(FileTransferStatus.Completed, item.Status);
            Assert.AreEqual(Lang.Get("lang.kitopia.saved"), item.StateText);
            Assert.AreEqual(path, item.LocalFilePath);
            Assert.AreEqual(1d, item.ReceiveProgress);
            Assert.IsTrue(item.CanUseLocalFile);
            Assert.IsFalse(item.IsReceiving);
            Assert.IsFalse(item.CanHandleIncomingOffer);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void IncomingFile_ProgressWithoutLocalAccept_UpdatesCardAndHidesOfferActions()
    {
        using var discovery = new FakeDiscoveryService(new DiscoveredDevice { Id = "peer-1", Name = "Phone" });
        var transferId = Guid.NewGuid();
        using var viewModel = new DeviceCommunicationPageViewModel(discovery,
            new FakeMessageAppService(incomingEvents: new[]
            {
                FileEvent(transferId, FileTransferStatus.WaitingForAccept),
                FileEvent(transferId, FileTransferStatus.InProgress),
                FileEvent(transferId, FileTransferStatus.Accepted)
            }), new FakeChatAttachmentStore(), new FakeChatPlatformService(), new FakeDeviceCommunicationSettings(),
            new FakeToastService(), postToUi: action => action());
        var item = viewModel.Conversations.Single().Messages.OfType<FileChatMessageItem>().Single();
        Assert.AreEqual(FileTransferStatus.InProgress, item.Status);
        StringAssert.Contains(item.StateText, "50%");
        Assert.IsTrue(item.IsReceiving);
        Assert.IsTrue(item.IsTransferActive);
        Assert.IsFalse(item.CanHandleIncomingOffer);
    }

    [TestMethod]
    public async Task IncomingFile_CompletesWhileAcceptCallIsPending_PreservesSavedStateAndPreview()
    {
        var transferId = Guid.NewGuid();
        var path = Path.GetTempFileName();
        var acceptReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var discovery = new FakeDiscoveryService(new DiscoveredDevice { Id = "peer-1", Name = "Phone" });
        var messages = new FakeMessageAppService(FileEvent(transferId, FileTransferStatus.WaitingForAccept))
        {
            AcceptAction = () => new ValueTask(acceptReturned.Task)
        };
        using var viewModel = new DeviceCommunicationPageViewModel(discovery, messages,
            new FakeChatAttachmentStore { SaveTarget = ChatFileSaveTarget.FromLocalPath(path) },
            new FakeChatPlatformService(), new FakeDeviceCommunicationSettings(), new FakeToastService(),
            postToUi: action => action());
        var conversation = viewModel.Conversations.Single();
        var item = conversation.Messages.OfType<FileChatMessageItem>().Single();
        item.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(item.Status) && item.Status == FileTransferStatus.Completed)
                completed.TrySetResult();
        };
        try
        {
            var accept = viewModel.AcceptIncomingOfferCommand.ExecuteAsync(item);
            Assert.AreEqual(FileTransferStatus.Accepted, item.Status);
            Assert.IsTrue(messages.Events.Writer.TryWrite(FileEvent(transferId, FileTransferStatus.Completed) with { LocalFilePath = path }));
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            acceptReturned.SetResult();
            await accept;

            Assert.AreEqual(FileTransferStatus.Completed, item.Status);
            Assert.AreEqual(1d, item.ReceiveProgress);
            Assert.IsTrue(item.CanUseLocalFile);
            StringAssert.Contains(conversation.LastMessagePreview, Lang.Get("lang.kitopia.saved"));
        }
        finally
        {
            acceptReturned.TrySetResult();
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task CancelTransfer_AfterSelectingAnotherConversation_CancelsCardConversation()
    {
        using var discovery = new FakeDiscoveryService(new DiscoveredDevice { Id = "peer-1", Name = "Phone" },
            new DiscoveredDevice { Id = "peer-2", Name = "Tablet" });
        var messages = new FakeMessageAppService(FileEvent(Guid.NewGuid(), FileTransferStatus.WaitingForAccept));
        using var viewModel = new DeviceCommunicationPageViewModel(discovery, messages,
            new FakeChatAttachmentStore(), new FakeChatPlatformService(), new FakeDeviceCommunicationSettings(),
            new FakeToastService(), postToUi: action => action());
        var conversation = viewModel.Conversations.Single(value => value.DeviceId == "peer-1");
        var item = conversation.Messages.OfType<FileChatMessageItem>().Single();
        viewModel.SelectedConversation = viewModel.Conversations.Single(value => value.DeviceId == "peer-2");

        await viewModel.CancelTransferCommand.ExecuteAsync(item);

        Assert.AreEqual(("peer-1", item.TrackingTransferId!.Value), messages.CancelledTransfer);
        Assert.AreEqual(FileTransferStatus.Cancelled, item.Status);
        StringAssert.Contains(conversation.LastMessagePreview, item.StateText);
    }

    [TestMethod]
    [DataRow(280, "zh-CN", false)]
    [DataRow(360, "en-US", true)]
    [DataRow(1200, "zh-CN", false)]
    public async Task ChatPage_LongTextAndPortraitImage_FitViewportAndPreviewWorks(int width, string language, bool dark)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(DeviceCommunicationPageViewModelResumeTests));
        await session.Dispatch(() =>
        {
            var originalLanguage = Lang.Current.Language;
            Lang.Current.UseLanguage(language);
            Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            using var discovery = new FakeDiscoveryService(new DiscoveredDevice
            {
                Id = "peer-1", Name = "Desktop-with-a-very-long-computer-name", CustomName = "Long device name for layout verification",
                OperatingSystem = "Windows", Ipv4Address = System.Net.IPAddress.Parse("192.168.1.100"),
                Ipv6Address = System.Net.IPAddress.Parse("2001:db8:1234:5678:9012:abcd:ef01:2345")
            });
            using var viewModel = new DeviceCommunicationPageViewModel(discovery, new FakeMessageAppService(),
                new FakeChatAttachmentStore(), new FakeChatPlatformService(), new FakeDeviceCommunicationSettings(),
                new FakeToastService(), postToUi: action => action());
            var conversation = viewModel.Conversations.Single();
            var text = string.Concat(Enumerable.Repeat("Long-text-without-spaces-", 8));
            conversation.Messages.Add(new DeviceChatMessageItem(text, false, DateTimeOffset.Now));
            conversation.Messages.Add(new DeviceChatMessageItem(text, true, DateTimeOffset.Now) { IsFailed = true });
            using var source = new OpenCvSharp.Mat(800, 160, OpenCvSharp.MatType.CV_8UC3, new OpenCvSharp.Scalar(64, 164, 72));
            OpenCvSharp.Cv2.ImEncode(".png", source, out var imageBytes);
            var image = DeviceChatMessageItem.CreateImage(imageBytes, false, DateTimeOffset.Now);
            conversation.Messages.Add(image);
            var page = new DeviceCommunicationPage { DataContext = viewModel };
            var window = new Window { Width = width, Height = 900, Content = page };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var list = page.FindControl<ItemsControl>("ConversationItemsControl")!;
                foreach (var control in list.GetVisualDescendants().OfType<Control>()
                             .Where(value => value.IsEffectivelyVisible && value is TextBlock or SelectableTextBlock or Image))
                {
                    var position = control.TranslatePoint(default, list)!.Value;
                    Assert.IsTrue(position.X >= -0.5 && position.X + control.Bounds.Width <= list.Bounds.Width + 0.5,
                        $"{control.GetType().Name} exceeds message viewport at {width}.");
                }
                using var frame = window.CaptureRenderedFrame();
                Assert.IsNotNull(frame);
                var screenshotDirectory = Path.Combine(AppContext.BaseDirectory, "TestResults", "lan-chat-display");
                Directory.CreateDirectory(screenshotDirectory);
                var screenshot = Path.Combine(screenshotDirectory, $"chat-page-{language}-{width}-{dark}.png");
                frame.Save(screenshot, PngBitmapEncoderOptions.Default);
                TestContext.AddResultFile(screenshot);

                var thumbnail = list.GetVisualDescendants().OfType<Image>().Single(value => value.IsEffectivelyVisible);
                Assert.IsTrue(thumbnail.Bounds.Height <= 320);
                var point = thumbnail.TranslatePoint(new Point(thumbnail.Bounds.Width / 2, thumbnail.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                var overlay = page.FindControl<Border>("ImagePreviewOverlay")!;
                Assert.IsTrue(overlay.IsVisible);
                Assert.AreSame(image.ImagePreview, page.FindControl<Image>("ImagePreviewContent")!.Source);
                var title = page.FindControl<TextBlock>("ImagePreviewTitle")!;
                var toolbar = overlay.GetVisualDescendants().OfType<StackPanel>().Single();
                Assert.IsTrue(title.Bounds.Width <= overlay.Bounds.Width);
                Assert.IsTrue(toolbar.Bounds.Width <= overlay.Bounds.Width - overlay.Padding.Left - overlay.Padding.Right);
                Assert.IsTrue(title.TranslatePoint(default, overlay)!.Value.Y + title.Bounds.Height <= toolbar.TranslatePoint(default, overlay)!.Value.Y);
                overlay.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                Assert.IsFalse(overlay.IsVisible);

                var flyout = (MenuFlyout)thumbnail.ContextFlyout!;
                flyout.ShowAt(thumbnail);
                Dispatcher.UIThread.RunJobs();
                var enlarge = flyout.Items.OfType<MenuItem>().Single(value => Equals(value.Header, Lang.Get("lang.kitopia.enlarge")));
                Assert.IsNotNull(enlarge.Command);
                enlarge.Command.Execute(enlarge.CommandParameter);
                flyout.Hide();
                Assert.IsTrue(overlay.IsVisible);
                viewModel.SelectedConversation = null;
                Assert.IsFalse(overlay.IsVisible);
                Assert.IsNull(image.PreviewImageCommand);
                Dispatcher.UIThread.RunJobs();
                foreach (var label in page.GetVisualDescendants().OfType<TextBlock>().Where(value => value.IsEffectivelyVisible))
                {
                    var position = label.TranslatePoint(default, page)!.Value;
                    Assert.IsTrue(position.X >= -0.5 && position.X + label.Bounds.Width <= page.Bounds.Width + 0.5,
                        $"{label.Text} exceeds conversation list at {width}.");
                }
            }
            finally
            {
                window.Close();
                image.ImagePreview?.Dispose();
                Lang.Current.UseLanguage(originalLanguage);
            }
        }, CancellationToken.None);
    }

    private static FileTransferUpdatedEvent FileEvent(Guid transferId, FileTransferStatus status)
        => new("peer-1", transferId, FileTransferDirection.Download, status, "archive.zip", 50, 100,
            null, DateTimeOffset.UtcNow);

    [TestMethod]
    public async Task FileCard_DoubleTap_OpensOnlyCompletedFile()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(DeviceCommunicationPageViewModelResumeTests));
        await session.Dispatch(() =>
        {
            var path = Path.GetTempFileName();
            using var discovery = new FakeDiscoveryService(new DiscoveredDevice { Id = "peer-1", Name = "Phone" });
            var platform = new FakeChatPlatformService { CanOpenFile = true };
            using var viewModel = new DeviceCommunicationPageViewModel(discovery, new FakeMessageAppService(),
                new FakeChatAttachmentStore(), platform, new FakeDeviceCommunicationSettings(), new FakeToastService(),
                postToUi: action => action());
            var item = new FileChatMessageItem("received.zip", 100, false, DateTimeOffset.Now)
            {
                ConversationId = "peer-1", TrackingTransferId = Guid.NewGuid(), LocalFilePath = path,
                Status = FileTransferStatus.Completed
            };
            viewModel.Conversations.Single().Messages.Add(item);
            var page = new DeviceCommunicationPage { DataContext = viewModel };
            var window = new Window { Width = 500, Height = 600, Content = page };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var label = page.GetVisualDescendants().OfType<TextBlock>()
                    .Single(value => value.IsEffectivelyVisible && value.Text == item.FileName);
                var point = label.TranslatePoint(new Point(5, 5), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                Assert.AreEqual(path, platform.OpenedFile);

                platform.OpenedFile = null;
                item.Status = FileTransferStatus.InProgress;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                Assert.IsNull(platform.OpenedFile);
            }
            finally
            {
                window.Close();
                File.Delete(path);
            }
        }, CancellationToken.None);
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
        public Channel<DeviceMessageEvent> Events { get; } = Channel.CreateUnbounded<DeviceMessageEvent>();
        public Func<ValueTask>? AcceptAction { get; init; }
        public (string DeviceId, Guid TransferId)? CancelledTransfer { get; private set; }
        private readonly DeviceMessageEvent? _incomingEvent;
        private readonly IncomingMessageDisplayMode _displayMode;
        private readonly IReadOnlyList<DeviceMessageEvent>? _incomingEvents;

        public FakeMessageAppService(
            DeviceMessageEvent? incomingEvent = null,
            IncomingMessageDisplayMode displayMode = IncomingMessageDisplayMode.ShowInCurrentConversation,
            IReadOnlyList<DeviceMessageEvent>? incomingEvents = null)
        {
            _incomingEvent = incomingEvent;
            _displayMode = displayMode;
            _incomingEvents = incomingEvents;
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
        public ValueTask AcceptFileAsync(string deviceId, Guid transferId, string saveTarget, Func<CancellationToken, ValueTask<Stream>> openWriteStreamAsync, CancellationToken cancellationToken = default) => AcceptAction?.Invoke() ?? ValueTask.CompletedTask;
        public ValueTask RejectFileAsync(string deviceId, Guid transferId, string reason, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask CancelTransferAsync(string deviceId, Guid transferId, string reason, CancellationToken cancellationToken = default)
        {
            CancelledTransfer = (deviceId, transferId);
            return ValueTask.CompletedTask;
        }
        public async IAsyncEnumerable<DeviceMessageEvent> ReceiveAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (_incomingEvent is not null)
            {
                yield return _incomingEvent;
            }
            if (_incomingEvents is not null)
            {
                foreach (var incomingEvent in _incomingEvents) yield return incomingEvent;
            }

            await foreach (var incomingEvent in Events.Reader.ReadAllAsync(cancellationToken)) yield return incomingEvent;
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
        public bool CanOpenFile { get; init; }
        public string? OpenedFile { get; set; }
        public void OpenFile(string path) => OpenedFile = path;
        public Task<string?> PromptTextAsync(string title, string prompt, string? initialValue) => Task.FromResult<string?>(null);
        public ChatDisplayContext GetDisplayContext(string? selectedConversationId) => new(true, true);
    }

    private sealed class FakeChatAttachmentStore : IChatAttachmentStore
    {
        public ChatFileSaveTarget? SaveTarget { get; init; }
        public Task<IReadOnlyList<string>> PickFilesToSendAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        public Task<ChatFileSaveTarget?> PickSaveTargetAsync(
            string suggestedFileName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SaveTarget);
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
