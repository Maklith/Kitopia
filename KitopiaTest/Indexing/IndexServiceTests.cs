using Kitopia.Desktop.Features.Indexing;
using Kitopia.Desktop.Features.Services.Config;
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
}
