using System.Reflection;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Search;
using Kitopia.Desktop.Platform.Windows.Everything;
using PluginCore.Config;

namespace KitopiaTest.AppTools;

[TestClass]
[DoNotParallelize]
public sealed class EverythingSearchTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SdkOperation_CanceledWhileWaitingForGate_ExitsWithoutAcquiringGate(bool availabilityCheck)
    {
        var gate = (SemaphoreSlim)typeof(EverythingClient)
            .GetField("QueryGate", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await gate.WaitAsync();
        Task? search = null;
        try
        {
            search = Task.Run(() =>
            {
                started.SetResult();
                if (availabilityCheck) EverythingClient.IsAvailable(cancellation.Token);
                else EverythingClient.Search("unused", 0, 100, cancellationToken: cancellation.Token);
            });
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await search.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.AreEqual(0, gate.CurrentCount);
        }
        finally
        {
            cancellation.Cancel();
            gate.Release();
            if (search is not null)
            {
                try { await search; }
                catch (OperationCanceledException) { }
            }
        }
    }

    [TestMethod]
    [DataRow(EverythingSortOrder.NameAscending, 1u)]
    [DataRow(EverythingSortOrder.NameDescending, 2u)]
    [DataRow(EverythingSortOrder.PathAscending, 3u)]
    [DataRow(EverythingSortOrder.PathDescending, 4u)]
    [DataRow(EverythingSortOrder.SizeAscending, 5u)]
    [DataRow(EverythingSortOrder.SizeDescending, 6u)]
    [DataRow(EverythingSortOrder.DateModifiedAscending, 13u)]
    [DataRow(EverythingSortOrder.DateModifiedDescending, 14u)]
    public void Search_WithSortOrder_SetsEverythingSort(EverythingSortOrder sortOrder, uint expected)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Everything requires Windows.");
            return;
        }

        var previousConfigs = ConfigManger.Configs;
        ConfigManger.Configs = new Dictionary<string, ConfigBase> { ["KitopiaConfig"] = new KitopiaConfig() };
        try
        {
            Action search = () => EverythingClient.Search("kitopia-sort-test-" + Guid.NewGuid().ToString("N"), 0, 1, sortOrder);
            if (EverythingClient.IsAvailable()) search();
            else Assert.ThrowsExactly<InvalidOperationException>(search);
            var sort = IntPtr.Size == 8 ? EverythingNative64.Everything_GetSort() : EverythingNative32.Everything_GetSort();
            Assert.AreEqual(expected, sort);
        }
        finally
        {
            if (IntPtr.Size == 8) EverythingNative64.Everything_Reset();
            else EverythingNative32.Everything_Reset();
            ConfigManger.Configs = previousConfigs;
        }
    }

    [TestMethod]
    public void Search_CaseSettingChanges_UpdatesEverythingMatchCaseOnNextQuery()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Everything requires Windows.");
            return;
        }

        var previousConfigs = ConfigManger.Configs;
        var config = new KitopiaConfig();
        ConfigManger.Configs = new Dictionary<string, ConfigBase> { ["KitopiaConfig"] = config };
        try
        {
            foreach (var ignoreCase in new[] { true, false, true })
            {
                config.everythingSearchIgnoreCase = ignoreCase;
                // SDK settings remain inspectable even when no Everything server accepts the query.
                Action search = () => EverythingClient.Search("kitopia-case-test-" + Guid.NewGuid().ToString("N"), 1);
                if (EverythingClient.IsAvailable()) search();
                else Assert.ThrowsExactly<InvalidOperationException>(search);
                var matchCase = IntPtr.Size == 8
                    ? EverythingNative64.Everything_GetMatchCase()
                    : EverythingNative32.Everything_GetMatchCase();
                Assert.AreEqual(!ignoreCase, matchCase);
            }
        }
        finally
        {
            if (IntPtr.Size == 8) EverythingNative64.Everything_Reset();
            else EverythingNative32.Everything_Reset();
            ConfigManger.Configs = previousConfigs;
        }
    }

    [TestMethod]
    public void Search_WithOffsetAndCount_SetsEverythingOffsetAndMax()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Everything requires Windows.");
            return;
        }

        var previousConfigs = ConfigManger.Configs;
        var config = new KitopiaConfig();
        ConfigManger.Configs = new Dictionary<string, ConfigBase> { ["KitopiaConfig"] = config };
        try
        {
            Action search = () => EverythingClient.Search("kitopia-page-test-" + Guid.NewGuid().ToString("N"), 100, 50);
            if (EverythingClient.IsAvailable()) search();
            else Assert.ThrowsExactly<InvalidOperationException>(search);
            var offset = IntPtr.Size == 8
                ? EverythingNative64.Everything_GetOffset()
                : EverythingNative32.Everything_GetOffset();
            var maxResults = IntPtr.Size == 8
                ? EverythingNative64.Everything_GetMax()
                : EverythingNative32.Everything_GetMax();

            Assert.AreEqual(100u, offset);
            Assert.AreEqual(50u, maxResults);
        }
        finally
        {
            if (IntPtr.Size == 8) EverythingNative64.Everything_Reset();
            else EverythingNative32.Everything_Reset();
            ConfigManger.Configs = previousConfigs;
        }
    }
}
