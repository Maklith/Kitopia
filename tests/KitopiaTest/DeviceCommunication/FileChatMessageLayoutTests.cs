using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Feature.Avalonia.DeviceCommunication.ViewModels;
using Kitopia.Feature.Avalonia.DeviceCommunication.Views;
using Kitopia.Feature.DeviceCommunication.Application;
using Kitopia.Feature.Localization;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
[DoNotParallelize]
public sealed class FileChatMessageLayoutTests
{
    public TestContext TestContext { get; set; } = null!;

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Kitopia.Desktop.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

    [TestMethod]
    [DataRow(600, "zh-CN", false)]
    [DataRow(280, "en-US", false)]
    [DataRow(280, "zh-CN", true)]
    [DataRow(600, "en-US", true)]
    public async Task FileCards_LongNamesAndTransferStates_FitViewport(int width, string language, bool dark)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(FileChatMessageLayoutTests));
        await session.Dispatch(() =>
        {
            var originalLanguage = Lang.Current.Language;
            Lang.Current.UseLanguage(language);
            Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var items = new[]
            {
                new FileChatMessageItem("project-archive-with-a-very-long-file-name-for-layout-verification.zip", 10_485_760, false, DateTimeOffset.Now)
                {
                    TrackingTransferId = Guid.NewGuid(),
                    AcceptCommand = new RelayCommand<FileChatMessageItem>(_ => { }),
                    RejectCommand = new RelayCommand<FileChatMessageItem>(_ => { })
                },
                new FileChatMessageItem("receiving.zip", 10_485_760, false, DateTimeOffset.Now)
                {
                    Status = FileTransferStatus.InProgress,
                    ReceiveProgress = 0.45, TransferSpeedBytesPerSecond = 2.5 * 1024 * 1024
                },
                new FileChatMessageItem("saved.pdf", 1024, false, DateTimeOffset.Now)
                {
                    Status = FileTransferStatus.Completed
                },
                new FileChatMessageItem("failed.zip", 1024, false, DateTimeOffset.Now)
                {
                    Status = FileTransferStatus.Failed
                },
                new FileChatMessageItem("rejected.zip", 1024, false, DateTimeOffset.Now)
                {
                    Status = FileTransferStatus.Rejected
                },
                new FileChatMessageItem("cancelled.zip", 1024, false, DateTimeOffset.Now)
                {
                    Status = FileTransferStatus.Cancelled
                },
                new FileChatMessageItem("timed-out.zip", 1024, false, DateTimeOffset.Now)
                {
                    Status = FileTransferStatus.Timeout
                },
                new FileChatMessageItem("outgoing-archive-with-a-very-long-file-name-for-layout-verification.zip", 10_485_760, true, DateTimeOffset.Now)
                {
                    Status = FileTransferStatus.InProgress, ReceiveProgress = 0.45, TransferSpeedBytesPerSecond = 2.5 * 1024 * 1024
                },
                new FileChatMessageItem("README", 1024, true, DateTimeOffset.Now)
                {
                    Status = FileTransferStatus.Delivered
                }
            };
            var page = new DeviceCommunicationPage();
            var list = new ItemsControl
            {
                ItemsSource = items,
                ItemTemplate = new ChatMessageTemplateSelector
                {
                    FileMessageTemplate = (IDataTemplate)page.FindResource("FileChatMessageTemplate")!
                }
            };
            page.Content = new ScrollViewer
            {
                Padding = new Thickness(12), Content = list,
                Background = (IBrush)Application.Current.FindResource("SemiColorFill0")!,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            };
            var window = new Window { Width = width, Height = 900, Content = page };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame();
                Assert.IsNotNull(frame);
                var screenshotDirectory = Path.Combine(AppContext.BaseDirectory, "TestResults", "lan-chat-display");
                Directory.CreateDirectory(screenshotDirectory);
                var screenshot = Path.Combine(screenshotDirectory, $"file-cards-{language}-{width}-{dark}.png");
                frame.Save(screenshot, PngBitmapEncoderOptions.Default);
                TestContext.AddResultFile(screenshot);

                var cards = list.GetVisualDescendants().OfType<Border>()
                    .Where(border => border.IsEffectivelyVisible && border.Parent is Grid { Name: "FileMessageRoot" })
                    .ToArray();
                Assert.HasCount(items.Length, cards);
                foreach (var card in cards)
                {
                    var item = (FileChatMessageItem)card.DataContext!;
                    Assert.IsTrue(card.Background is ISolidColorBrush { Color.A: > 0 });
                    Assert.IsTrue(card.Bounds.Width > 180 && card.Bounds.Width <= Math.Min(320, list.Bounds.Width));
                    var cardPosition = card.TranslatePoint(default, list)!.Value;
                    Assert.IsTrue(cardPosition.X >= 0 && cardPosition.X + card.Bounds.Width <= list.Bounds.Width + 0.5);
                    foreach (var control in card.GetVisualDescendants().OfType<Control>()
                                 .Where(control => control.IsEffectivelyVisible && control is TextBlock or Button or ProgressBar))
                    {
                        var position = control.TranslatePoint(default, card)!.Value;
                        Assert.IsTrue(position.X >= 0 && position.X + control.Bounds.Width <= card.Bounds.Width + 0.5,
                            $"{item.FileName}: {control.GetType().Name} exceeds card width.");
                    }
                    var buttons = card.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).ToArray();
                    Assert.HasCount(item.CanHandleIncomingOffer ? 2 : 0, buttons);
                    Assert.IsTrue(buttons.All(button => ReferenceEquals(item, button.CommandParameter)));
                    Assert.IsTrue(card.GetVisualDescendants().OfType<TextBlock>()
                        .Any(text => text.IsEffectivelyVisible && text.Text == item.FileTypeText));
                }
            }
            finally
            {
                window.Close();
                Lang.Current.UseLanguage(originalLanguage);
            }
        }, CancellationToken.None);
    }

    [TestMethod]
    public async Task IncomingCard_WaitingToCompleted_UpdatesStatusProgressAndActions()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(FileChatMessageLayoutTests));
        await session.Dispatch(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
            FileChatMessageItem? cancelledItem = null;
            var item = new FileChatMessageItem("archive.zip", 1024, false, DateTimeOffset.Now)
            {
                TrackingTransferId = Guid.NewGuid(), LocalFilePath = path,
                CancelCommand = new RelayCommand<FileChatMessageItem>(value => cancelledItem = value)
            };
            var page = new DeviceCommunicationPage();
            page.Content = new ItemsControl
            {
                ItemsSource = new[] { item },
                ItemTemplate = new ChatMessageTemplateSelector
                {
                    FileMessageTemplate = (IDataTemplate)page.FindResource("FileChatMessageTemplate")!
                }
            };
            var window = new Window { Width = 400, Height = 300, Content = page };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var card = window.GetVisualDescendants().OfType<Border>()
                    .Single(border => border.IsEffectivelyVisible && border.Parent is Grid { Name: "FileMessageRoot" });
                var progress = card.GetVisualDescendants().OfType<ProgressBar>().Single();
                var state = card.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.Text == Lang.Get("lang.kitopia.waiting_to_receive"));
                Assert.IsFalse(progress.IsVisible);
                Assert.IsTrue(item.CanHandleIncomingOffer);
                var flyout = (MenuFlyout)card.ContextFlyout!;
                flyout.ShowAt(card);
                Dispatcher.UIThread.RunJobs();
                var open = flyout.Items.OfType<MenuItem>().Single(menu => Equals(menu.Header, Lang.Get("lang.kitopia.open")));
                var cancel = flyout.Items.OfType<MenuItem>().Single(menu => Equals(menu.Header, Lang.Get("lang.kitopia.cancel_transfer")));
                Assert.IsFalse(open.IsEnabled);

                item.Status = FileTransferStatus.Accepted;
                Dispatcher.UIThread.RunJobs();
                Assert.IsTrue(progress.IsVisible);
                Assert.AreEqual(item.StateText, state.Text);
                Assert.IsFalse(item.CanHandleIncomingOffer);

                item.Status = FileTransferStatus.InProgress;
                item.ReceiveProgress = 0.5;
                item.TransferSpeedBytesPerSecond = 2.5 * 1024 * 1024;
                Dispatcher.UIThread.RunJobs();
                Assert.IsTrue(progress.IsVisible);
                Assert.AreEqual(0.5, progress.Value);
                Assert.AreEqual(Lang.Format("lang.kitopia.receiving_value_value", "2.5 MB/s", "50"), state.Text);
                Assert.IsTrue(cancel.IsVisible);
                cancel.Command!.Execute(cancel.CommandParameter);
                Assert.AreSame(item, cancelledItem);
                Assert.IsFalse(open.IsEnabled);
                Assert.IsFalse(item.CanHandleIncomingOffer);

                File.WriteAllText(path, "received");
                item.Status = FileTransferStatus.Completed;
                Dispatcher.UIThread.RunJobs();
                Assert.IsFalse(progress.IsVisible);
                Assert.AreEqual(Lang.Get("lang.kitopia.saved"), state.Text);
                Assert.IsTrue(open.IsEnabled);
                Assert.IsFalse(cancel.IsVisible);
            }
            finally
            {
                window.Close();
                File.Delete(path);
            }
        }, CancellationToken.None);
    }
}
