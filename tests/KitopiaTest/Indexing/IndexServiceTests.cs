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
    public async Task RebuildFiles_WithoutSemanticPlugin_PreservesVectorsAndReportsDownloadGuidance()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"kitopia-missing-semantic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var store = new IndexVectorStore(Path.Combine(directory, "index.db"));
        var index = new IndexService();
        typeof(IndexService).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(index, store);
        try
        {
            Assert.IsFalse(EmbeddingGemmaModelPackage.IsComplete());
            await store.UpsertTextChunksAsync("document.txt", "test-model", [new float[EmbeddingGemmaEmbeddingService.VectorDimensions]], CancellationToken.None);
            await store.UpsertImageAsync("image.png", "fingerprint", "test-model", new float[EmbeddingGemmaEmbeddingService.VectorDimensions], CancellationToken.None);
            await index.RebuildAsync(IndexRebuildScope.Files);
            var status = index.GetStatus();
            Assert.IsFalse(status.IsRebuilding);
            Assert.AreEqual("lang.kitopia.semantic_model_missing", status.LastError);
            Assert.AreEqual(0, status.FailedImages);
            Assert.AreEqual((1, 1), await store.GetCountsAsync(CancellationToken.None));
        }
        finally
        {
            var queuedField = typeof(IndexService).GetField("_statusPublishQueued", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while ((int)queuedField.GetValue(index)! != 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            index.Dispose();
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
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
            var queuedField = typeof(IndexService).GetField("_statusPublishQueued", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while ((int)queuedField.GetValue(index)! != 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            index.Dispose();
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task SearchPinyin_ManagedFiles_MatchesChinesePinyinAndAsciiAndInvalidatesOnSync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"kitopia-managed-pinyin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var chineseFile = Path.Combine(directory, "测试报告.docx");
        var asciiFile = Path.Combine(directory, "AnnualReport.pdf");
        var ignoredWord = Path.Combine(directory, "UnrelatedNote.txt");
        await File.WriteAllTextAsync(chineseFile, "cn");
        await File.WriteAllTextAsync(asciiFile, "en");
        await File.WriteAllTextAsync(ignoredWord, "skip");

        var store = new IndexVectorStore(Path.Combine(directory, "index.db"));
        var index = new IndexService();
        typeof(IndexService).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(index, store);
        try
        {
            await index.SynchronizeFilesAsync([chineseFile, asciiFile, ignoredWord], IndexSource.EverythingManaged, CancellationToken.None);

            var pinyinFull = index.SearchPinyin("ceshi", 100, CancellationToken.None);
            Assert.AreEqual(1, pinyinFull.Count);
            Assert.AreEqual(chineseFile, pinyinFull[0].Source.OnlyKey);
            Assert.IsNotNull(pinyinFull[0].CharMatchResults);

            var pinyinInitials = index.SearchPinyin("csbg", 100, CancellationToken.None);
            Assert.AreEqual(1, pinyinInitials.Count);
            Assert.AreEqual(chineseFile, pinyinInitials[0].Source.OnlyKey);

            // Second search reuses the cached managed-file snapshot even while the store write gate is held.
            var gate = (SemaphoreSlim)typeof(IndexVectorStore).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
            await gate.WaitAsync(CancellationToken.None);
            try
            {
                var asciiMatches = index.SearchPinyin("anrep", 100, CancellationToken.None);
                Assert.AreEqual(1, asciiMatches.Count);
                Assert.AreEqual(asciiFile, asciiMatches[0].Source.OnlyKey);
            }
            finally
            {
                gate.Release();
            }

            await index.SynchronizeFilesAsync([], IndexSource.EverythingManaged, CancellationToken.None);
            Assert.IsEmpty(index.SearchPinyin("ceshi", 100, CancellationToken.None));
        }
        finally
        {
            await DisposeAndDeleteAsync(index, directory);
        }
    }

    [TestMethod]
    public async Task SearchPinyin_ExplicitEntries_SingleCharCutoffAndImmediateRemovalWorkCleanly()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"kitopia-explicit-pinyin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var store = new IndexVectorStore(Path.Combine(directory, "index.db"));
        var index = new IndexService();
        typeof(IndexService).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(index, store);
        try
        {
            for (var i = 0; i < 1100; i++)
            {
                var name = i < 30 ? $"A_Top_{i:D2}" : $"zzzz_tail_{i:D4}_a";
                index.TryAdd(new SearchEntry { OnlyKey = $"app:{i}", DisplayName = name, FileType = FileType.应用程序 });
            }
            index.TryAdd(new SearchEntry { OnlyKey = "app:cn", DisplayName = "测试应用", FileType = FileType.应用程序 });

            await index.RebuildPinyinSearcherAsync();

            var singleChar = index.SearchPinyin("a", 5, CancellationToken.None);
            Assert.AreEqual(5, singleChar.Count);
            Assert.IsTrue(singleChar.All(item => item.Source.DisplayName.StartsWith("A_Top_", StringComparison.Ordinal)));

            var cn = index.SearchPinyin("csyy", 5, CancellationToken.None);
            Assert.AreEqual(1, cn.Count);
            Assert.AreEqual("app:cn", cn[0].Source.OnlyKey);
            CollectionAssert.AreEqual(new[] { true, true, true, true }, cn[0].CharMatchResults);

            Assert.IsTrue(index.TryRemove("app:0"));
            var afterRemove = index.SearchPinyin("a", 5, CancellationToken.None);
            Assert.IsFalse(afterRemove.Any(item => item.Source.OnlyKey == "app:0"));
        }
        finally
        {
            await DisposeAndDeleteAsync(index, directory);
        }
    }

    [TestMethod]
    public async Task SearchPinyin_ManagedFiles_ParallelEquivalenceWithPinyinSearcher_AndHighlightMask()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"kitopia-managed-parallel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var realFiles = new[]
        {
            Path.Combine(directory, "银行流水_v2-final.xlsx"),
            Path.Combine(directory, "my_app-v2.0.pdf"),
            Path.Combine(directory, "设计规范文档.md"),
            Path.Combine(directory, "AnnualReport2026.docx")
        };
        foreach (var path in realFiles)
        {
            await File.WriteAllTextAsync(path, "sample");
        }

        // > 1024 managed entries exercises the parallel partition scan path.
        var allPaths = new List<string>(1500);
        allPaths.AddRange(realFiles);
        for (var i = 0; i < 1496; i++)
        {
            allPaths.Add(Path.Combine(directory, $"unrelated_item_{i}.txt"));
        }

        var store = new IndexVectorStore(Path.Combine(directory, "index.db"));
        var index = new IndexService();
        typeof(IndexService).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(index, store);
        try
        {
            await index.SynchronizeFilesAsync(allPaths, IndexSource.EverythingManaged, CancellationToken.None);
            var referenceSearcher = new Pinyin.NET.PinyinSearcher<string>(
                realFiles,
                path => Path.GetFileNameWithoutExtension(path));

            foreach (var query in new[] { "yhls", "hangls", "shej", "my v2", "anrep", "银行 v2" })
            {
                var actual = index.SearchPinyin(query, 100, CancellationToken.None);
                var expected = referenceSearcher.Search(query, 100, CancellationToken.None);
                CollectionAssert.AreEquivalent(
                    expected.Select(e => e.Source).ToArray(),
                    actual.Select(a => a.Source.OnlyKey).ToArray(),
                    $"Query '{query}' mismatched reference PinyinSearcher.");
                foreach (var item in actual)
                {
                    var expectedMatch = expected.Single(e => e.Source == item.Source.OnlyKey);
                    CollectionAssert.AreEqual(expectedMatch.CharMatchResults, item.CharMatchResults,
                        $"Highlight mask mismatch for '{query}' on '{item.Source.DisplayName}'.");
                }
            }

            Assert.IsEmpty(index.SearchPinyin("zzzz_nomatch", 100, CancellationToken.None));
        }
        finally
        {
            await DisposeAndDeleteAsync(index, directory);
        }
    }

    [TestMethod]
    public async Task SearchPinyin_EquivalenceWithPinyinSearcher_ComplexQueriesCutoffAndIgnoreFilter()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"kitopia-pinyin-diff-{Guid.NewGuid():N}");
        var ignoredDir = Path.Combine(directory, "ignored_dir");
        var lateIgnoredDir = Path.Combine(directory, "late_ignored_dir");
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(ignoredDir);
        Directory.CreateDirectory(lateIgnoredDir);

        var realManagedFiles = new[]
        {
            Path.Combine(directory, "alpha_beta_gamma_delta_epsilon_zeta_eta_theta_iota_kappa_lambda_mu_nu_xi_omicron_pi_rho_sigma_tau_upsilon_phi_chi_psi_omega_end.txt"),
            Path.Combine(directory, "veryLongCamelCaseIdentifierWithoutExpectedSubstringTokenAnywhereInTheMiddleTail.txt"),
            Path.Combine(directory, "foo`bar_baz.txt"),
            Path.Combine(directory, "MySuperAppV2Release.txt"),
            Path.Combine(directory, "项目_Report2026-v3_最终版.docx"),
            Path.Combine(directory, "银行123测试.txt"),
            Path.Combine(directory, "测试报告2026.pdf"),
            Path.Combine(directory, "cccccc_a_match.txt")
        };
        var ignoredManagedFile = Path.Combine(ignoredDir, "MySuperAppV2Ignored.txt");
        var lateIgnoredManagedFile = Path.Combine(lateIgnoredDir, "MySuperAppV2LateIgnored.txt");
        foreach (var path in realManagedFiles.Concat([ignoredManagedFile, lateIgnoredManagedFile]))
        {
            await File.WriteAllTextAsync(path, "sample");
        }

        var allManagedPaths = new List<string>(1150);
        allManagedPaths.AddRange(realManagedFiles);
        allManagedPaths.Add(ignoredManagedFile);
        allManagedPaths.Add(lateIgnoredManagedFile);
        for (var i = 0; i < 1100; i++)
        {
            allManagedPaths.Add(Path.Combine(directory, $"tail_alpha_beta_{i:D4}_x.txt"));
        }

        ConfigManger.Config.ignoreItems.Add(ignoredDir);

        var store = new IndexVectorStore(Path.Combine(directory, "index.db"));
        var index = new IndexService();
        typeof(IndexService).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(index, store);
        try
        {
            var explicitDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["app:cn1"] = "支付宝账单2026",
                ["app:long"] = "veryLongExplicitNameWithoutToken_matchHere",
                ["app:q0"] = "q0",
                ["app:q1"] = "xq1",
                ["app:q2"] = "xxq2",
                ["app:q3"] = "xxxq3",
                ["app:q4"] = "xxxxq4",
                ["app:q5"] = "xxxxx_q5_tail"
            };
            foreach (var (key, displayName) in explicitDisplayNames)
            {
                index.TryAdd(new SearchEntry { OnlyKey = key, DisplayName = displayName, FileType = FileType.应用程序 });
            }

            var lateIgnoredExplicitKey = Path.Combine(lateIgnoredDir, "LateIgnoredApp.exe");
            index.TryAdd(new SearchEntry { OnlyKey = lateIgnoredExplicitKey, DisplayName = "MySuperAppV2ExplicitIgnored", FileType = FileType.应用程序 });
            await index.RebuildPinyinSearcherAsync();

            await index.SynchronizeFilesAsync(allManagedPaths, IndexSource.EverythingManaged, CancellationToken.None);

            // Add lateIgnoredDir after syncing to exercise runtime ignore filtering during SearchPinyin.
            ConfigManger.Config.ignoreItems.Add(lateIgnoredDir);

            var referenceItems = new Dictionary<string, string>(explicitDisplayNames, StringComparer.OrdinalIgnoreCase);
            foreach (var path in realManagedFiles)
            {
                referenceItems[path] = Path.GetFileNameWithoutExtension(path);
            }

            var referenceSearcher = new Pinyin.NET.PinyinSearcher<string>(
                referenceItems.Keys,
                key => referenceItems[key]);

            foreach (var query in new[]
                     {
                         "alpha omega",
                         "long tail",
                         "here",
                         "foobar",
                         "mysuv2",
                         "xm2026",
                         "v3 zzb",
                         "zfbzd 2026",
                         "支付宝 2026",
                         "yh 123",
                         "银行 123",
                         "csbg 2026",
                         "z",
                         "zzzz_nomatch"
                     })
            {
                var actual = index.SearchPinyin(query, 100, CancellationToken.None);
                var expected = referenceSearcher.Search(query, 100, CancellationToken.None);
                CollectionAssert.AreEquivalent(
                    expected.Select(e => e.Source).ToArray(),
                    actual.Select(a => a.Source.OnlyKey).ToArray(),
                    $"Query '{query}' mismatched reference PinyinSearcher.");
                Assert.IsFalse(actual.Any(item =>
                    item.Source.OnlyKey.Equals(ignoredManagedFile, StringComparison.OrdinalIgnoreCase)
                    || item.Source.OnlyKey.Equals(lateIgnoredManagedFile, StringComparison.OrdinalIgnoreCase)
                    || item.Source.OnlyKey.Equals(lateIgnoredExplicitKey, StringComparison.OrdinalIgnoreCase)),
                    $"Ignored entry appeared for query '{query}'.");
                foreach (var item in actual)
                {
                    var expectedMatch = expected.Single(e => e.Source == item.Source.OnlyKey);
                    CollectionAssert.AreEqual(expectedMatch.CharMatchResults, item.CharMatchResults,
                        $"Highlight mask mismatch for '{query}' on '{item.Source.DisplayName}'.");
                }
            }

            var cutoff = index.SearchPinyin("q", 5, CancellationToken.None);
            CollectionAssert.AreEqual(
                new[] { "app:q0", "app:q1", "app:q2", "app:q3", "app:q4" },
                cutoff.Select(result => result.Source.OnlyKey).ToArray());
        }
        finally
        {
            await DisposeAndDeleteAsync(index, directory);
        }
    }

    [TestMethod]
    public async Task SynchronizeFilesAsync_YieldsGateBetweenBatches_WarmsSnapshot_AndSingleFlightSurvivesCallerCancellation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"kitopia-singleflight-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var managedFile = Path.Combine(directory, "ManagedBatchTarget.txt");
        await File.WriteAllTextAsync(managedFile, "content");

        var store = new IndexVectorStore(Path.Combine(directory, "index.db"));
        var index1 = new IndexService();
        typeof(IndexService).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(index1, store);
        IndexService? index2 = null;
        try
        {
            index1.TryAdd(new SearchEntry { OnlyKey = "app:explicit", DisplayName = "ExplicitApp", FileType = FileType.应用程序 });
            await index1.RebuildPinyinSearcherAsync();

            var batchFlushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            IEnumerable<string> SlowManagedPaths()
            {
                yield return managedFile;
                for (var i = 0; i < 259; i++)
                {
                    if (i == 258)
                    {
                        batchFlushed.TrySetResult();
                        allowFinish.Task.GetAwaiter().GetResult();
                    }

                    yield return Path.Combine(directory, $"batch_placeholder_{i:D3}.txt");
                }
            }

            var syncTask = Task.Run(() =>
                index1.SynchronizeFilesAsync(SlowManagedPaths(), IndexSource.EverythingManaged, CancellationToken.None));
            await batchFlushed.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Concurrent search must not deadlock behind SynchronizeFilesAsync while enumerating.
            var explicitResults = await Task.Run(() => index1.SearchPinyin("explicit", 10, CancellationToken.None))
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(explicitResults.Any(r => r.Source.OnlyKey == "app:explicit"));

            allowFinish.SetResult();
            Assert.IsTrue(await syncTask.WaitAsync(TimeSpan.FromSeconds(5)));

            // SynchronizeFilesAsync warmed _managedFilesSnapshot, so searching succeeds with _gate held.
            var gate = (SemaphoreSlim)typeof(IndexVectorStore).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
            await gate.WaitAsync(CancellationToken.None);
            try
            {
                var warmed = index1.SearchPinyin("mbtarget", 10, CancellationToken.None);
                Assert.AreEqual(1, warmed.Count);
                Assert.AreEqual(managedFile, warmed[0].Source.OnlyKey);
            }
            finally
            {
                gate.Release();
            }

            // Cold single-flight load on index2: canceling caller 1 must not abort caller 2.
            index2 = new IndexService();
            typeof(IndexService).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(index2, store);
            var loadTaskField = typeof(IndexService).GetField("_managedFilesLoadTask", BindingFlags.Instance | BindingFlags.NonPublic)!;

            await gate.WaitAsync(CancellationToken.None);
            var gateReleased = false;
            try
            {
                using var cts1 = new CancellationTokenSource();
                using var cts2 = new CancellationTokenSource();
                var search1 = Task.Run(() => index2.SearchPinyin("mbtarget", 10, cts1.Token));

                var deadline = DateTime.UtcNow.AddSeconds(5);
                Task? sharedLoad = null;
                while (sharedLoad is null && DateTime.UtcNow < deadline)
                {
                    sharedLoad = (Task?)loadTaskField.GetValue(index2);
                    if (sharedLoad is null) await Task.Delay(10);
                }
                Assert.IsNotNull(sharedLoad);

                var search2 = Task.Run(() => index2.SearchPinyin("mbtarget", 10, cts2.Token));
                await Task.Delay(50);
                Assert.AreSame(sharedLoad, loadTaskField.GetValue(index2));

                cts1.Cancel();
                await Assert.ThrowsAsync<OperationCanceledException>(async () => await search1.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.IsFalse(search2.IsCompleted);

                gate.Release();
                gateReleased = true;

                var caller2Results = await search2.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.AreEqual(1, caller2Results.Count);
                Assert.AreEqual(managedFile, caller2Results[0].Source.OnlyKey);
            }
            finally
            {
                if (!gateReleased)
                {
                    gate.Release();
                }
            }
        }
        finally
        {
            if (index2 is not null)
            {
                var queuedField = typeof(IndexService).GetField("_statusPublishQueued", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while ((int)queuedField.GetValue(index2)! != 0 && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(10);
                }

                index2.Dispose();
            }

            await DisposeAndDeleteAsync(index1, directory);
        }
    }

    private static async Task DisposeAndDeleteAsync(IndexService index, string directory)
    {
        var queuedField = typeof(IndexService).GetField("_statusPublishQueued", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while ((int)queuedField.GetValue(index)! != 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        index.Dispose();
        SqliteConnection.ClearAllPools();
        Directory.Delete(directory, recursive: true);
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
