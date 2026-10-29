using System.Reflection;
using Kitopia.Desktop.Features.Indexing;
using Kitopia.Desktop.Features.Search;
using Kitopia.Desktop.Features.Search.Semantic;
using Kitopia.Desktop.Features.Services.Config;
using Microsoft.Data.Sqlite;
using PluginCore;
using PluginCore.Config;

namespace KitopiaTest.Indexing;

[TestClass]
[DoNotParallelize]
public sealed class IndexServiceTests
{
    private Dictionary<string, ConfigBase>? _originalConfigs;

    [TestInitialize]
    public void Initialize()
    {
        _originalConfigs = ConfigManger.Configs;
        ConfigManger.Configs = new Dictionary<string, ConfigBase>
        {
            ["KitopiaConfig"] = new KitopiaConfig { Name = "KitopiaConfig" }
        };
    }

    [TestCleanup]
    public void Cleanup()
    {
        ConfigManger.Configs = _originalConfigs!;
    }

    [TestMethod]
    public async Task SearchPinyin_EverythingApplication_AppearsAfterOrdinaryIndexBuild()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"kitopia-ordinary-search-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var index = new IndexService();
        typeof(IndexService).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(index, new IndexVectorStore(Path.Combine(directory, "index.db")));
        try
        {
            index.TryAdd(new SearchEntry { OnlyKey = @"C:\Apps\Everything.exe", DisplayName = "Everything", FileType = FileType.应用程序 });
            Assert.IsFalse(index.SearchPinyin("every", 100, CancellationToken.None)
                .Any(result => result.Source.DisplayName == "Everything"));
            await index.RebuildPinyinSearcherAsync();
            var results = index.SearchPinyin("every", 100, CancellationToken.None);
            Assert.IsTrue(results.Any(result => result.Source.DisplayName == "Everything"));
        }
        finally
        {
            index.Dispose();
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task SearchAsync_PreparedLexicalResults_DoesNotRepeatFileDatabaseScan()
    {
        using var index = new IndexService();
        var store = (IndexVectorStore)typeof(IndexService).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(index)!;
        var gate = (SemaphoreSlim)typeof(IndexVectorStore).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
        IReadOnlyList<SearchIndexResult> lexicalResults = Enumerable.Range(0, 10).Select(number =>
            new SearchIndexResult(new SearchEntry
            {
                OnlyKey = $"report{number}.txt", DisplayName = $"Report {number}", FileType = FileType.文件
            }, 1d / (61 + number), null)).ToArray();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await gate.WaitAsync(cancellation.Token);
        try
        {
            var results = await index.SearchAsync("report", 100, cancellation.Token, lexicalResults)
                .WaitAsync(cancellation.Token);
            CollectionAssert.AreEqual(lexicalResults.ToArray(), results.ToArray());
        }
        finally
        {
            gate.Release();
        }
    }

    [TestMethod]
    public async Task GetEntriesSnapshot_EmbeddingInitializationInProgress_DoesNotHoldApplicationIndexLock()
    {
        using var index = new IndexService();
        using var resume = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var field = typeof(IndexService).GetField("_embeddingService", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(index, new Lazy<EmbeddingGemmaEmbeddingService?>(() =>
        {
            started.SetResult();
            resume.Wait(cancellation.Token);
            return null;
        }));
        var initialize = Task.Run(() =>
        {
            object?[] arguments = [null];
            return (bool)typeof(IndexService).GetMethod("TryGetEmbeddingService", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(index, arguments)!;
        });
        try
        {
            await started.Task.WaitAsync(cancellation.Token);
            var results = await Task.Run(index.GetEntriesSnapshot).WaitAsync(cancellation.Token);
            Assert.HasCount(0, results);
            Assert.IsFalse(initialize.IsCompleted);
        }
        finally
        {
            resume.Set();
            Assert.IsFalse(await initialize.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        Assert.IsNull(field.GetValue(index), "An unavailable model must be retried after it is downloaded.");
    }

    [TestMethod]
    public void ShouldAutomaticallyIndexFile_DefaultNamesExcludeMinecraftAssets()
    {
        var path = Path.Combine("root", ".minecraft", "assets", "readme.md");

        Assert.IsFalse(IndexService.ShouldAutomaticallyIndexFile(path));
        Assert.IsFalse(IndexService.ShouldAutomaticallyIndexEverythingFile(path));
        Assert.IsFalse(IndexService.ShouldAutomaticallyIndexFile(
            Path.Combine("root", "assets", "readme.md")));
    }

    [TestMethod]
    public void ShouldAutomaticallyIndexFile_IgnoresConfiguredPathAndDescendants()
    {
        var ignoredRoot = Path.Combine(Environment.CurrentDirectory, "KitopiaIgnoredDirectory");
        ConfigManger.Config.ignoreItems.Add(ignoredRoot + Path.DirectorySeparatorChar);

        Assert.IsFalse(IndexService.ShouldAutomaticallyIndexFile(
            Path.Combine(ignoredRoot, "child", "report.pdf")));
        Assert.IsFalse(IndexService.ShouldAutomaticallyIndexEverythingFile(
            Path.Combine(ignoredRoot, "child", "report.txt")));
        Assert.IsTrue(IndexService.ShouldAutomaticallyIndexFile(
            ignoredRoot + "2" + Path.DirectorySeparatorChar + "report.pdf"));

        if (OperatingSystem.IsWindows())
        {
            Assert.IsFalse(IndexService.ShouldAutomaticallyIndexFile(
                Path.Combine(ignoredRoot.ToUpperInvariant(), "child", "report.pdf")));
        }
    }

    [TestMethod]
    public void ShouldAutomaticallyIndexFile_DefaultExtensionsExcludeUnwantedFiles()
    {
        Assert.IsFalse(IndexService.ShouldAutomaticallyIndexFile(Path.Combine("root", "program.exe")));
        Assert.IsFalse(IndexService.ShouldAutomaticallyIndexFile(Path.Combine("root", "state.dat")));
        Assert.IsTrue(IndexService.ShouldAutomaticallyIndexFile(Path.Combine("root", "manual.pdf")));
    }

    [TestMethod]
    public void ShouldAutomaticallyIndexFile_UsesConfiguredNames()
    {
        ConfigManger.Config.transientDirectoryNames.Clear();
        ConfigManger.Config.transientDirectoryNames.Add("generated");

        Assert.IsFalse(IndexService.ShouldAutomaticallyIndexFile(
            Path.Combine("root", "generated", "file.pdf")));
        Assert.IsTrue(IndexService.ShouldAutomaticallyIndexFile(
            Path.Combine("root", "cache", "file.pdf")));
    }

    [TestMethod]
    public void ShouldAutomaticallyIndexFile_UsesConfiguredExtensions()
    {
        ConfigManger.Config.allowedFileExtensions.Clear();
        ConfigManger.Config.allowedFileExtensions.Add("*.txt");

        Assert.IsTrue(IndexService.ShouldAutomaticallyIndexFile(Path.Combine("root", "notes.txt")));
        Assert.IsFalse(IndexService.ShouldAutomaticallyIndexFile(Path.Combine("root", "notes.pdf")));
    }

    [TestMethod]
    public void ShouldAutomaticallyIndexEverythingFile_IgnoresAllowedExtensions()
    {
        ConfigManger.Config.allowedFileExtensions.Clear();
        ConfigManger.Config.allowedFileExtensions.Add("*.pdf");

        Assert.IsFalse(IndexService.ShouldAutomaticallyIndexFile(Path.Combine("root", "notes.txt")));
        Assert.IsTrue(IndexService.ShouldAutomaticallyIndexEverythingFile(Path.Combine("root", "notes.txt")));
    }

    [TestMethod]
    public async Task RunPausableStepAsync_ForegroundPause_CancelsAndRetriesCurrentStep()
    {
        using var index = new IndexService();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var operation = index.RunPausableStepAsync(async token =>
        {
            if (Interlocked.Increment(ref attempts) > 1) return;
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
                cancelled.SetResult();
                throw;
            }
        }, CancellationToken.None);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        index.SetForegroundPause(true);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(operation.IsCompleted);

        index.SetForegroundPause(false);
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(2, attempts);
    }

    [TestMethod]
    public async Task RunPausableStepAsync_PausedBeforeStart_WaitsForResume()
    {
        using var index = new IndexService();
        index.SetForegroundPause(true);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = index.RunPausableStepAsync(_ =>
        {
            started.SetResult();
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.IsFalse(started.Task.IsCompleted);
        index.SetForegroundPause(false);
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(started.Task.IsCompleted);
    }

    [TestMethod]
    public async Task PauseIndexing_AlreadyForegroundPaused_WaitsForManualResume()
    {
        using var index = new IndexService();
        using var operationCancellation = new CancellationTokenSource();
        typeof(IndexService).GetField("_activeOperationCancellation", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(index, operationCancellation);
        index.SetForegroundPause(true);
        var started = false;
        var operation = index.RunPausableStepAsync(_ =>
        {
            started = true;
            return Task.CompletedTask;
        }, operationCancellation.Token);

        index.PauseIndexing();
        index.SetForegroundPause(false);
        Assert.IsFalse(started);
        index.ResumeIndexing();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(started);
    }
}
