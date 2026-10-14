using System.Reflection;
using System.Threading.Channels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Kitopia.Desktop.Features.Indexing;
using Kitopia.Desktop.Features.Search;
using Kitopia.Desktop.Features.Search.ViewModels;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Windows;
using Kitopia.Feature.Localization;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Config;

namespace KitopiaTest.Search;

[TestClass]
[DoNotParallelize]
public sealed class SemanticSearchStatusTests
{
    public TestContext TestContext { get; set; } = null!;

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Kitopia.Desktop.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia();

    [TestMethod]
    [DataRow("complete")]
    [DataRow("fail")]
    [DataRow("cancel")]
    public async Task ToSearch_SemanticSearchEnds_HidesIndicator(string outcome)
    {
        var screenshots = new List<string>();
        await RunSearchTestAsync(async (model, index, view) =>
        {
            var result = new TaskCompletionSource<IReadOnlyList<SearchIndexResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
            index.Search = (_, _) => result.Task;
            var transitions = Channel.CreateUnbounded<bool>();
            model.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(model.IsSemanticSearching)) return;
                Assert.IsTrue(Dispatcher.UIThread.CheckAccess());
                transitions.Writer.TryWrite(model.IsSemanticSearching);
            };

            model.Search = "first";
            Assert.IsFalse(model.IsSemanticSearching);
            await index.Started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(await transitions.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            Dispatcher.UIThread.RunJobs();
            var indicator = view.FindControl<Border>("SemanticSearchIndicator")!;
            var progress = view.FindControl<ProgressBar>("SemanticSearchProgress")!;
            Assert.IsTrue(indicator.IsVisible);
            Assert.IsTrue(progress.IsIndeterminate);
            Assert.AreEqual("Lexical result", model.Items.Single().ItemDisplayName);

            if (outcome == "complete")
            {
                var window = (Window)TopLevel.GetTopLevel(indicator)!;
                foreach (var language in new[] { "zh-CN", "en-US" })
                foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    Lang.Current.UseLanguage(language);
                    Application.Current!.RequestedThemeVariant = theme;
                    window.RequestedThemeVariant = theme;
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Assert.IsGreaterThan(0, indicator.Bounds.Width);
                    Assert.IsGreaterThan(0, progress.Bounds.Width);
                    using var frame = window.CaptureRenderedFrame();
                    Assert.IsNotNull(frame);
                    var screenshot = Path.Combine(TestContext.TestRunDirectory!, $"semantic-search-{language}-{theme}.png");
                    frame.Save(screenshot, PngBitmapEncoderOptions.Default);
                    screenshots.Add(screenshot);
                }
                result.SetResult(index.PinyinResults);
            }
            else if (outcome == "fail")
            {
                result.SetException(new InvalidOperationException("Search failure"));
            }
            else
            {
                result.SetCanceled();
            }

            Assert.IsFalse(await transitions.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            Dispatcher.UIThread.RunJobs();
            Assert.IsFalse(indicator.IsVisible);
            Assert.IsFalse(progress.IsIndeterminate);
        });
        foreach (var screenshot in screenshots) TestContext.AddResultFile(screenshot);
    }

    [TestMethod]
    public async Task ToSearch_QueryReplaced_OldCompletionDoesNotHideNewIndicatorOrRestoreClearedResults()
    {
        await RunSearchTestAsync(async (model, index, view) =>
        {
            var first = new TaskCompletionSource<IReadOnlyList<SearchIndexResult>>();
            var second = new TaskCompletionSource<IReadOnlyList<SearchIndexResult>>();
            index.Search = (query, _) => query == "first" ? first.Task : second.Task;
            var transitions = Channel.CreateUnbounded<bool>();
            model.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(model.IsSemanticSearching))
                    transitions.Writer.TryWrite(model.IsSemanticSearching);
            };

            model.Search = "first";
            var firstStarted = await index.Started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(await transitions.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));

            model.Search = "second";
            Assert.IsTrue(firstStarted.Token.IsCancellationRequested);
            Assert.IsFalse(await transitions.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            await index.Started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(await transitions.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));

            // Inline continuations let RunJobs drain the old task's UI updates deterministically.
            first.SetResult([]);
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(model.IsSemanticSearching);
            Assert.IsFalse(transitions.Reader.TryRead(out _));

            model.Search = string.Empty;
            Assert.IsFalse(model.IsSemanticSearching);
            Dispatcher.UIThread.RunJobs();
            second.SetResult(index.PinyinResults);
            Dispatcher.UIThread.RunJobs();
            Assert.IsFalse(model.IsSemanticSearching);
            Assert.HasCount(0, model.Items);
        });
    }

    private static async Task RunSearchTestAsync(Func<SearchWindowViewModel, SearchServiceProxy, SearchWindow, Task> test)
    {
        ReactiveUI.Builder.RxAppBuilder.CreateReactiveUIBuilder().WithPlatformServices().BuildApp();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SemanticSearchStatusTests));
        await session.Dispatch(async () =>
        {
            var originalConfigs = ConfigManger.Configs;
            var originalServices = ServiceManager.Services;
            var originalLanguage = Lang.Current.Language;
            var identifiers = PluginOverall.SearchWindowInputDataIdentifies.ToArray();
            var analyzers = PluginOverall.SearchWindowInputDataAnalyzers.ToArray();
            ConfigManger.Configs = new Dictionary<string, ConfigBase>
            {
                ["KitopiaConfig"] = new KitopiaConfig { useEverything = false, semanticSearchDebounceMilliseconds = 100 }
            };
            PluginOverall.SearchWindowInputDataIdentifies.Clear();
            PluginOverall.SearchWindowInputDataAnalyzers.Clear();
            PluginOverall.SearchWindowInputDataIdentifies["Kitopia"] = [];
            PluginOverall.SearchWindowInputDataAnalyzers["Kitopia"] = [];
            var index = DispatchProxy.Create<IIndexService, SearchServiceProxy>();
            var proxy = (SearchServiceProxy)index;
            using var services = new ServiceCollection().AddSingleton(index)
                .AddSingleton(DispatchProxy.Create<IAppToolService, SearchServiceProxy>()).BuildServiceProvider();
            ServiceManager.Services = services;
            var model = new SearchWindowViewModel();
            var view = new SearchWindow { DataContext = model };
            var content = view.Content;
            view.Content = null;
            var window = new Window { Content = content, DataContext = model, Width = 800, Height = 300 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                await test(model, proxy, view);
            }
            finally
            {
                model.Search = string.Empty;
                Dispatcher.UIThread.RunJobs();
                model.FilePreview.Dispose();
                window.Close();
                ServiceManager.Services = originalServices;
                ConfigManger.Configs = originalConfigs;
                Lang.Current.UseLanguage(originalLanguage);
                PluginOverall.SearchWindowInputDataIdentifies.Clear();
                PluginOverall.SearchWindowInputDataAnalyzers.Clear();
                foreach (var pair in identifiers) PluginOverall.SearchWindowInputDataIdentifies[pair.Key] = pair.Value;
                foreach (var pair in analyzers) PluginOverall.SearchWindowInputDataAnalyzers[pair.Key] = pair.Value;
            }
            return true;
        }, CancellationToken.None);
    }

    private class SearchServiceProxy : DispatchProxy
    {
        public IReadOnlyList<SearchIndexResult> PinyinResults { get; } =
        [new(new SearchEntry { OnlyKey = "https://example.com", DisplayName = "Lexical result", FileType = FileType.URL }, 1, null)];

        public Channel<(string Query, CancellationToken Token)> Started { get; } = Channel.CreateUnbounded<(string, CancellationToken)>();
        public Func<string, CancellationToken, Task<IReadOnlyList<SearchIndexResult>>> Search { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(IAppToolService.LoadIcon)) return null;
            if (targetMethod.Name == nameof(IIndexService.SearchPinyin)) return PinyinResults;
            if (targetMethod.Name != nameof(IIndexService.SearchAsync)) throw new NotSupportedException(targetMethod.Name);
            var query = (string)args![0]!;
            var token = (CancellationToken)args[2]!;
            Started.Writer.TryWrite((query, token));
            return Search(query, token);
        }
    }
}
