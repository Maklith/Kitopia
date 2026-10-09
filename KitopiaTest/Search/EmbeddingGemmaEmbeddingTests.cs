using System.Reflection;
using System.Text.Json;
using Kitopia.Desktop.Features.Indexing;
using Kitopia.Desktop.Features.Ocr;
using Kitopia.Desktop.Features.Search.Semantic;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Plugin;
using Microsoft.Data.Sqlite;
using OpenCvSharp;
using PluginCore.Config;
using PluginCore.Onnx;

namespace KitopiaTest.Search;

[TestClass]
[DoNotParallelize]
public sealed class EmbeddingGemmaEmbeddingTests
{
    private string _referenceDirectory = null!;
    private JsonDocument _reference = null!;
    private EmbeddingGemmaEmbeddingService _service = null!;
    private Dictionary<string, ConfigBase> _originalConfigs = null!;

    [TestInitialize]
    public void Initialize()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Kitopia.sln"))) root = root.Parent;
        Assert.IsNotNull(root);
        var models = Path.Combine(root.FullName, "Kitopia.Desktop.Features", "Assets", "EmbeddingGemma2");
        Assert.IsTrue(EmbeddingGemmaModelPackage.IsComplete(models), "The bundled Q4 graphs and external weight shards must be present.");
        _referenceDirectory = Path.Combine(root.FullName, "KitopiaTest", "Search", "EmbeddingGemmaReference");
        _reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(_referenceDirectory, "reference.json")));
        _originalConfigs = ConfigManger.Configs;
        ConfigManger.Configs = new Dictionary<string, ConfigBase>
        {
            ["KitopiaConfig"] = new KitopiaConfig
            {
                Name = "KitopiaConfig",
                OnnxTargetDevices = new Dictionary<string, string>
                {
                    [EmbeddingGemmaModelPackage.TextModelSignName] = "EG2-Test-CPU",
                    [EmbeddingGemmaModelPackage.VisionModelSignName] = "EG2-Test-CPU"
                }
            }
        };
        PluginOverall.OnnxRuntimes.Add("EG2-Test", new() { ["EG2-Test-CPU"] = () => new OnnxRuntime.CPU.MInferenceSession() });
        _service = new EmbeddingGemmaEmbeddingService(models);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _service?.Dispose();
        _reference?.Dispose();
        PluginOverall.OnnxRuntimes.Remove("EG2-Test");
        if (_originalConfigs is not null) ConfigManger.Configs = _originalConfigs;
    }

    [TestMethod]
    public void EncodeInput_MultilingualAndTruncatedInputs_MatchesOfficialTokenizer()
    {
        foreach (var row in _reference.RootElement.GetProperty("texts").EnumerateArray())
        {
            var expected = row.GetProperty("ids").EnumerateArray().Select(value => value.GetInt64()).ToArray();
            CollectionAssert.AreEqual(expected, _service.EncodeInput(row.GetProperty("text").GetString()!, row.GetProperty("limit").GetInt32()));
        }
        Assert.AreEqual("title: none | text: body", EmbeddingGemmaEmbeddingService.FormatDocument("body"));
        Assert.AreEqual("title: name | text: body", EmbeddingGemmaEmbeddingService.FormatDocument("body", "name"));
    }

    [TestMethod]
    public async Task EmbedAsync_PaddedBatchAndSessionReload_MatchesOfficialQ4Vectors()
    {
        var rows = _reference.RootElement.GetProperty("texts").EnumerateArray().ToArray();
        var texts = rows.Take(4).Select(row => row.GetProperty("text").GetString()!).ToArray();
        var vectors = await _service.EmbedAsync(texts, 8192, CancellationToken.None);
        for (var index = 0; index < vectors.Count; index++) AssertVector(rows[index], vectors[index], 0.999999);
        var truncated = await _service.EmbedAsync([rows[4].GetProperty("text").GetString()!], 24, CancellationToken.None);
        AssertVector(rows[4], truncated[0], 0.999999);
        await _service.ReleaseSessionsAsync();
        var reloaded = await _service.EmbedAsync([texts[0]], 512, CancellationToken.None);
        AssertVector(rows[0], reloaded[0], 0.999999);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => _service.EmbedAsync(texts, 512, canceled.Token));
    }

    [TestMethod]
    public async Task EmbedDocumentAsync_LongDocument_RetainsIndependentChunkVectors()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kitopia-eg2-chunks-{Guid.NewGuid():N}.md");
        try
        {
            var firstParagraph = string.Concat(Enumerable.Repeat("Apples grow in orchards. ", 200));
            var secondParagraph = string.Concat(Enumerable.Repeat("Satellites orbit the Earth. ", 80));
            await File.WriteAllTextAsync(path, firstParagraph + "\n\n" + secondParagraph);
            Assert.IsTrue(DocumentTextExtractor.TryCreateSource(path, out var source));
            var title = Path.GetFileName(path);
            var chunks = new List<string>();
            await foreach (var chunk in DocumentTextExtractor.ExtractChunksAsync(
                               source, text => _service.CountDocumentTokens(text, title), CancellationToken.None))
                chunks.Add(EmbeddingGemmaEmbeddingService.FormatDocument(chunk, title));
            Assert.IsGreaterThan(1, chunks.Count);
            Assert.IsTrue(chunks.All(chunk => _service.EncodeInput(chunk, 8192).Length <= 1024));
            var method = typeof(IndexService).GetMethod("EmbedDocumentAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
            var task = (Task<IReadOnlyList<float[]>>)method.Invoke(null, [source, _service, CancellationToken.None])!;
            var vectors = await task;
            Assert.HasCount(chunks.Count, vectors);
            foreach (var vector in vectors)
                Assert.AreEqual(1d, Math.Sqrt(vector.Sum(value => (double)value * value)), 1e-6);

            var expected = await _service.EmbedAsync(chunks, 1024, CancellationToken.None);
            for (var index = 0; index < vectors.Count; index++)
                Assert.IsTrue(vectors[index].Zip(expected[index], (left, right) => (double)left * right).Sum() > 0.99999);
            Assert.IsTrue(vectors[0].Zip(vectors[^1], (left, right) => (double)left * right).Sum() < 0.999);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task EmbedImagesAsync_PatchOrderPaddingAndVectors_MatchesOfficialProcessor()
    {
        foreach (var row in _reference.RootElement.GetProperty("images").EnumerateArray())
        {
            var path = Path.Combine(_referenceDirectory, row.GetProperty("file").GetString()!);
            var (pixels, positions, softTokens) = EmbeddingGemmaImageProcessor.Process(path);
            Assert.AreEqual(row.GetProperty("soft_tokens").GetInt32(), softTokens);
            var expectedPositions = row.GetProperty("positions").EnumerateArray()
                .SelectMany(position => position.EnumerateArray().Select(value => value.GetInt64())).ToArray();
            CollectionAssert.AreEqual(expectedPositions, positions);
            var offsets = row.GetProperty("pixel_offsets").EnumerateArray().Select(value => value.GetInt32()).ToArray();
            var expectedPixels = row.GetProperty("pixel_values").EnumerateArray().Select(value => value.GetSingle()).ToArray();
            var maximumDifference = offsets.Select((offset, index) => Math.Abs(pixels[offset] - expectedPixels[index])).Max();
            Assert.IsTrue(maximumDifference <= 2f / 255f, $"Pixel difference: {maximumDifference}");
            Assert.IsTrue(pixels.AsSpan(softTokens * 9 * 768).ToArray().All(value => value == 0));
            var vector = (await _service.EmbedImagesAsync([path], CancellationToken.None))[0];
            AssertVector(row, vector, 0.999);
        }
    }

    [TestMethod]
    [DataRow(1, 100000, 48, 13440)]
    [DataRow(100000, 1, 13440, 48)]
    [DataRow(800, 800, 768, 768)]
    public void GetTargetSize_ExtremeAspectRatios_StaysWithinOfficialPatchBudget(int width, int height, int expectedWidth, int expectedHeight)
    {
        Assert.AreEqual((expectedWidth, expectedHeight), EmbeddingGemmaImageProcessor.GetTargetSize(width, height));
    }

    [TestMethod]
    public async Task EmbedAsync_LongSequences_BoundsAttentionWithoutTruncatingDocuments()
    {
        using var probe = new SchedulingSession();
        PluginOverall.OnnxRuntimes["EG2-Test"]["EG2-Test-CPU"] = () => probe;
        var text = string.Concat(Enumerable.Repeat("Apples grow in orchards. ", 550));
        var length = _service.EncodeInput(text, 8192).Length;
        Assert.IsGreaterThan(2048, length);

        var vectors = await _service.EmbedAsync([text, text], 8192, CancellationToken.None);

        Assert.HasCount(2, vectors);
        CollectionAssert.AreEqual(new[] { (1, length), (1, length) }, probe.Batches.ToArray());
        Assert.AreEqual(Math.Max(1, Environment.ProcessorCount * 50 / 100), probe.WorkerCount);
        Assert.IsTrue(probe.UseCpuMemoryArena);
        Assert.AreEqual(8192L * 1024 * 1024, probe.GpuMemoryLimitBytes);
    }

    [TestMethod]
    public async Task EmbedAsync_QueryQueuedDuringIndexBatch_RunsBeforeNextBatch()
    {
        using var started = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var probe = new SchedulingSession
        {
            BeforeInference = count =>
            {
                if (count != 1) return;
                started.Set();
                resume.Wait(cancellation.Token);
            }
        };
        PluginOverall.OnnxRuntimes["EG2-Test"]["EG2-Test-CPU"] = () => probe;
        var indexing = _service.EmbedAsync(Enumerable.Repeat("Indexed document", 16).ToArray(), 512, cancellation.Token);
        Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));
        var query = _service.EmbedAsync(["task: search result | query: hello"], 512, cancellation.Token);
        resume.Set();
        await Task.WhenAll(indexing, query);

        CollectionAssert.AreEqual(new[] { 4, 1, 4, 4, 4 }, probe.Batches.Select(batch => batch.Count).ToArray());
    }

    [TestMethod]
    [DataRow(512, 4, 4)]
    [DataRow(1024, 4, 1)]
    public async Task EmbedAsync_IndexingInputs_BoundsBatchAttention(int length, int count, int batchSize)
    {
        using var probe = new SchedulingSession();
        PluginOverall.OnnxRuntimes["EG2-Test"]["EG2-Test-CPU"] = () => probe;
        var text = string.Concat(Enumerable.Repeat(" apple", length - 2));
        Assert.AreEqual(length, _service.EncodeInput(text, 1024).Length);

        var vectors = await _service.EmbedAsync(Enumerable.Repeat(text, count).ToArray(), 1024, CancellationToken.None);

        Assert.HasCount(count, vectors);
        Assert.IsTrue(probe.Batches.All(batch => batch.Count == batchSize && batch.Length == length));
    }

    [TestMethod]
    public async Task EmbedAsync_VaryingIndexingLengths_ReusesPaddingShapeAndMasksOnlyRealTokens()
    {
        using var probe = new SchedulingSession();
        PluginOverall.OnnxRuntimes["EG2-Test"]["EG2-Test-CPU"] = () => probe;
        var texts = new[] { 513, 750, 1000 }
            .Select(length => string.Concat(Enumerable.Repeat(" apple", length - 2))).ToArray();

        await _service.EmbedAsync(texts, 1024, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { (1, 1024), (1, 1024), (1, 1024) }, probe.Batches.ToArray());
        CollectionAssert.AreEqual(new[] { 513, 750, 1000 }, probe.MaskLengths.ToArray());
        for (var index = 0; index < texts.Length; index++)
        {
            var expected = _service.EncodeInput(texts[index], 1024);
            Assert.IsTrue(probe.InputRows[index].Span[..expected.Length].SequenceEqual(expected));
            Assert.IsTrue(probe.InputRows[index].Span[expected.Length..].IndexOfAnyExcept(0L) < 0);
        }
    }

    [TestMethod]
    [DataRow("Apples grow in orchards. ")]
    [DataRow("\u4e2d\u6587\u6587\u6863\u7d22\u5f15\u68c0\u7d22\u5185\u5bb9 ")]
    public async Task ExtractChunksAsync_MultilingualDocuments_BoundsActualTokensAndPreservesTail(string sentence)
    {
        var path = Path.Combine(Path.GetTempPath(), $"kitopia-eg2-token-budget-{Guid.NewGuid():N}.md");
        try
        {
            var text = "begin_marker " + string.Concat(Enumerable.Repeat(sentence, 600)) + " unique_tail";
            await File.WriteAllTextAsync(path, text);
            Assert.IsTrue(DocumentTextExtractor.TryCreateSource(path, out var source));
            var chunks = new List<string>();
            var title = Path.GetFileName(path);
            await foreach (var chunk in DocumentTextExtractor.ExtractChunksAsync(
                               source, content => _service.CountDocumentTokens(content, title), CancellationToken.None))
                chunks.Add(chunk);

            Assert.IsGreaterThan(1, chunks.Count);
            Assert.IsTrue(chunks.All(chunk => _service.CountDocumentTokens(chunk, title) <= 1024));
            StringAssert.StartsWith(chunks[0], "begin_marker");
            StringAssert.EndsWith(chunks[^1], "unique_tail");
            Assert.IsTrue(chunks.Sum(chunk => _service.CountDocumentTokens(chunk, title)) >= _service.CountDocumentTokens(text, title));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task IndexOcrTextAsync_LongOcr_IndexesTailInBoundedChunks()
    {
        var database = Path.Combine(Path.GetTempPath(), $"kitopia-ocr-chunks-{Guid.NewGuid():N}.db");
        using var probe = new SchedulingSession();
        PluginOverall.OnnxRuntimes["EG2-Test"]["EG2-Test-CPU"] = () => probe;
        var text = string.Concat(Enumerable.Repeat(" apple", 2000)) + " unique_tail";
        var expectedTail = _service.EncodeInput(EmbeddingGemmaEmbeddingService.FormatDocument(text), 8192).TakeLast(4).ToArray();
        try
        {
            var store = new IndexVectorStore(database);
            using var index = new IndexService(new FixedOcrService(text));
            typeof(IndexService).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(index, store);
            var method = typeof(IndexService).GetMethod("IndexOcrTextAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

            await (Task)method.Invoke(index, ["image.png", _service, CancellationToken.None])!;

            Assert.IsGreaterThan(1, probe.InputRows.Count);
            Assert.IsTrue(probe.Batches.All(batch => batch.Length <= 1024));
            var lastRow = probe.InputRows[^1].Span;
            var end = lastRow.LastIndexOf(1L) + 1;
            Assert.IsTrue(lastRow.Slice(end - 4, 4).SequenceEqual(expectedTail));
            Assert.IsTrue(await store.HasOcrTextVectorAsync("image.png", _service.ModelId, CancellationToken.None));
            Assert.AreEqual(probe.InputRows.Count, (await store.GetCountsAsync(CancellationToken.None)).TextVectors);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(database);
            File.Delete(database + "-shm");
            File.Delete(database + "-wal");
        }
    }

    [TestMethod]
    public async Task EmbedAsync_BackendChanged_DisposesPreviousSessionAndUsesNewRuntime()
    {
        using var previous = new SchedulingSession();
        using var replacement = new SchedulingSession();
        PluginOverall.OnnxRuntimes["EG2-Test"]["EG2-Test-CPU"] = () => previous;
        PluginOverall.OnnxRuntimes["EG2-Test"]["EG2-Test-Replacement"] = () => replacement;
        await _service.EmbedAsync(["hello"], 512, CancellationToken.None);
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "EG2-Test-Replacement";

        await _service.EmbedAsync(["hello"], 512, CancellationToken.None);

        Assert.IsTrue(previous.IsDisposed);
        Assert.HasCount(1, replacement.Batches);
        await _service.ReleaseSessionsAsync();
        Assert.IsTrue(replacement.IsDisposed);
    }

    [TestMethod]
    public async Task ReleaseSessionsAsync_InferenceActive_WaitsBeforeDisposingSession()
    {
        using var started = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var probe = new SchedulingSession
        {
            BeforeInference = _ =>
            {
                started.Set();
                resume.Wait(cancellation.Token);
            }
        };
        PluginOverall.OnnxRuntimes["EG2-Test"]["EG2-Test-CPU"] = () => probe;
        var inference = _service.EmbedAsync(["hello"], 512, cancellation.Token);
        Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)));

        var release = _service.ReleaseSessionsAsync();
        Assert.IsFalse(release.IsCompleted);
        Assert.IsFalse(probe.IsDisposed);
        resume.Set();
        await Task.WhenAll(inference, release);

        Assert.IsTrue(probe.IsDisposed);
    }

    [TestMethod]
    public async Task EmbedAsync_MultipleBatches_ReusesSessionUntilReleased()
    {
        var sessions = new List<SchedulingSession>();
        PluginOverall.OnnxRuntimes["EG2-Test"]["EG2-Test-CPU"] = () =>
        {
            var session = new SchedulingSession();
            sessions.Add(session);
            return session;
        };

        await _service.EmbedAsync(Enumerable.Repeat("Indexed document", 16).ToArray(), 512, CancellationToken.None);
        await _service.EmbedAsync(["task: search result | query: hello"], 512, CancellationToken.None);

        Assert.HasCount(1, sessions);
        Assert.HasCount(5, sessions[0].Batches);
        Assert.IsFalse(sessions[0].IsDisposed);
        await _service.ReleaseSessionsAsync();
        Assert.IsTrue(sessions[0].IsDisposed);
        await _service.EmbedAsync(["hello"], 512, CancellationToken.None);
        Assert.HasCount(2, sessions);
        Assert.IsFalse(sessions[1].IsDisposed);
    }

    [TestMethod]
    public async Task EmbedAsync_CudaAllocationFails_ReloadsOnceWithoutTruncatingAndReusesSession()
    {
        using var previous = new SchedulingSession
        {
            Device = "GPU(CUDA)",
            BeforeInference = count =>
            {
                if (count == 2) throw new OutOfMemoryException("Fragmented CUDA arena");
            }
        };
        using var replacement = new SchedulingSession { Device = "GPU(CUDA)" };
        var creations = 0;
        PluginOverall.OnnxRuntimes["EG2-Test"]["GPU(CUDA)"] = () => ++creations == 1 ? previous : replacement;
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "GPU(CUDA)";
        await _service.EmbedAsync(["hello"], 512, CancellationToken.None);
        var text = string.Concat(Enumerable.Repeat("Apples grow in orchards. ", 550));
        var length = _service.EncodeInput(text, 8192).Length;

        var vectors = await _service.EmbedAsync([text], 8192, CancellationToken.None);
        await _service.EmbedAsync(["hello"], 512, CancellationToken.None);

        Assert.HasCount(1, vectors);
        Assert.AreEqual(2, creations);
        Assert.IsTrue(previous.IsDisposed);
        Assert.IsFalse(replacement.IsDisposed);
        Assert.AreEqual((1, length), replacement.Batches[0]);
        Assert.HasCount(2, replacement.Batches);
    }

    [TestMethod]
    public async Task EmbedAsync_CudaAllocationStillFails_UsesCpuUntilReleasedThenRetriesConfiguredDevice()
    {
        var gpuSessions = new List<SchedulingSession>();
        PluginOverall.OnnxRuntimes["EG2-Test"]["GPU(CUDA)"] = () =>
        {
            var session = new SchedulingSession
            {
                Device = "GPU(CUDA)",
                BeforeInference = gpuSessions.Count < 2 ? _ => throw new OutOfMemoryException("CUDA arena limit") : null
            };
            gpuSessions.Add(session);
            return session;
        };
        using var cpu = new SchedulingSession();
        PluginOverall.OnnxRuntimes["EG2-Test"]["CPU"] = () => cpu;
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "GPU(CUDA)";

        var vectors = await _service.EmbedAsync(["hello"], 512, CancellationToken.None);
        await _service.EmbedAsync(["hello again"], 512, CancellationToken.None);

        Assert.HasCount(1, vectors);
        Assert.HasCount(2, gpuSessions);
        Assert.IsTrue(gpuSessions.All(session => session.IsDisposed));
        Assert.HasCount(2, cpu.Batches);
        Assert.AreEqual("GPU(CUDA)", ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName]);
        await _service.ReleaseSessionsAsync();
        Assert.IsTrue(cpu.IsDisposed);
        await _service.EmbedAsync(["hello"], 512, CancellationToken.None);
        Assert.HasCount(3, gpuSessions);
        Assert.HasCount(1, gpuSessions[2].Batches);
        Assert.IsFalse(gpuSessions[2].IsDisposed);
    }

    [TestMethod]
    public async Task EmbedAsync_CudaAllocationFailsWithoutCpu_StopsAfterOneReload()
    {
        var creations = 0;
        var failure = new OutOfMemoryException("CUDA arena limit");
        PluginOverall.OnnxRuntimes["EG2-Test"]["GPU(CUDA)"] = () =>
        {
            creations++;
            return new SchedulingSession { Device = "GPU(CUDA)", BeforeInference = _ => throw failure };
        };
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "GPU(CUDA)";

        var exception = await Assert.ThrowsAsync<OutOfMemoryException>(() => _service.EmbedAsync(["hello"], 512, CancellationToken.None));

        Assert.AreSame(failure, exception);
        Assert.AreEqual(2, creations);
    }

    [TestMethod]
    public async Task EmbedAsync_CanceledDuringCudaFailure_DoesNotReloadOrUseCpu()
    {
        using var cancellation = new CancellationTokenSource();
        using var gpu = new SchedulingSession
        {
            Device = "GPU(CUDA)",
            BeforeInference = _ =>
            {
                cancellation.Cancel();
                throw new OutOfMemoryException("CUDA arena limit");
            }
        };
        var creations = 0;
        PluginOverall.OnnxRuntimes["EG2-Test"]["GPU(CUDA)"] = () =>
        {
            creations++;
            return gpu;
        };
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "GPU(CUDA)";

        await Assert.ThrowsAsync<OperationCanceledException>(() => _service.EmbedAsync(["hello"], 512, cancellation.Token));

        Assert.AreEqual(1, creations);
        Assert.IsTrue(gpu.IsDisposed);
    }

    [TestMethod]
    public async Task EmbedAsync_NonAllocationCudaFailure_DoesNotRetry()
    {
        var creations = 0;
        var failure = new InvalidOperationException("Invalid model input");
        PluginOverall.OnnxRuntimes["EG2-Test"]["GPU(CUDA)"] = () =>
        {
            creations++;
            return new SchedulingSession { Device = "GPU(CUDA)", BeforeInference = _ => throw failure };
        };
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "GPU(CUDA)";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.EmbedAsync(["hello"], 512, CancellationToken.None));

        Assert.AreSame(failure, exception);
        Assert.AreEqual(1, creations);
    }

    [TestMethod]
    public async Task EmbedAsync_CpuAllocationFails_DoesNotRetry()
    {
        var creations = 0;
        PluginOverall.OnnxRuntimes["EG2-Test"]["EG2-Test-CPU"] = () =>
        {
            creations++;
            return new SchedulingSession { BeforeInference = _ => throw new OutOfMemoryException("CPU allocation failed") };
        };

        await Assert.ThrowsAsync<OutOfMemoryException>(() => _service.EmbedAsync(["hello"], 512, CancellationToken.None));

        Assert.AreEqual(1, creations);
    }

    [TestMethod]
    public async Task EmbedAsync_CudaModelLoadingAllocationFails_ReloadsOnceThenUsesCpu()
    {
        var failedSessions = new List<SchedulingSession>();
        PluginOverall.OnnxRuntimes["EG2-Test"]["GPU(CUDA)"] = () =>
        {
            var session = new SchedulingSession
            {
                Device = "GPU(CUDA)", BeforeInitialization = () => throw new OutOfMemoryException("CUDA model allocation failed")
            };
            failedSessions.Add(session);
            return session;
        };
        using var cpu = new SchedulingSession();
        PluginOverall.OnnxRuntimes["EG2-Test"]["CPU"] = () => cpu;
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "GPU(CUDA)";

        var vectors = await _service.EmbedAsync(["hello"], 512, CancellationToken.None);

        Assert.HasCount(1, vectors);
        Assert.HasCount(2, failedSessions);
        Assert.IsTrue(failedSessions.All(session => session.IsDisposed));
        Assert.IsTrue(failedSessions.All(session => session.Batches.Count == 0));
        Assert.HasCount(1, cpu.Batches);
    }

    [TestMethod]
    public async Task EmbedImagesAsync_CudaTextAllocationFails_ReleasesVisionSessionAndPreservesImageFeatures()
    {
        var row = _reference.RootElement.GetProperty("images")[0];
        var path = Path.Combine(_referenceDirectory, row.GetProperty("file").GetString()!);
        var featureLength = row.GetProperty("soft_tokens").GetInt32() * 512;
        using var vision = new SchedulingSession { Device = "GPU(CUDA)", OutputLength = featureLength };
        var failedTexts = new List<SchedulingSession>();
        using var cpu = new SchedulingSession();
        var textCreations = 0;
        PluginOverall.OnnxRuntimes["EG2-Test"]["GPU(CUDA)"] = () =>
        {
            if (textCreations++ == 0) return vision;
            var session = new SchedulingSession
            {
                Device = "GPU(CUDA)", BeforeInference = _ => throw new OutOfMemoryException("CUDA arena limit")
            };
            failedTexts.Add(session);
            return session;
        };
        PluginOverall.OnnxRuntimes["EG2-Test"]["CPU"] = () => cpu;
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.TextModelSignName] = "GPU(CUDA)";
        ConfigManger.Config.OnnxTargetDevices[EmbeddingGemmaModelPackage.VisionModelSignName] = "GPU(CUDA)";

        var vectors = await _service.EmbedImagesAsync([path], CancellationToken.None);

        Assert.HasCount(1, vectors);
        Assert.IsTrue(vision.IsDisposed);
        Assert.HasCount(2, failedTexts);
        Assert.IsTrue(failedTexts.All(session => session.IsDisposed));
        Assert.HasCount(1, cpu.Batches);
        Assert.AreEqual(featureLength, cpu.ImageFeatureLengths[0]);
    }

    private sealed class SchedulingSession : IInferenceSession
    {
        public string Device { get; init; } = "CPU";
        public IReadOnlyList<string> InputNames => [];
        public IReadOnlyList<int[]> OutputShape => [];
        public List<(int Count, int Length)> Batches { get; } = [];
        public List<ReadOnlyMemory<long>> InputRows { get; } = [];
        public List<int> MaskLengths { get; } = [];
        public List<int> ImageFeatureLengths { get; } = [];
        public int? OutputLength { get; init; }
        public Action<int>? BeforeInference { get; init; }
        public Action? BeforeInitialization { get; init; }
        public int WorkerCount { get; private set; }
        public bool UseCpuMemoryArena { get; private set; }
        public long GpuMemoryLimitBytes { get; private set; }
        public bool IsDisposed { get; private set; }
        public void InitSession(string modelPath) => throw new NotSupportedException();
        public void InitSession(byte[] modelData) => throw new NotSupportedException();
        public void InitSession(string modelPath, bool useCpuMemoryArena, int intraOpNumThreads, long gpuMemoryLimitBytes)
        {
            BeforeInitialization?.Invoke();
            UseCpuMemoryArena = useCpuMemoryArena;
            WorkerCount = intraOpNumThreads;
            GpuMemoryLimitBytes = gpuMemoryLimitBytes;
        }
        public Memory<float> Infer(List<(string, Memory<int>, Memory<float>)> inputs) => throw new NotSupportedException();
        public Memory<float> Infer(List<(string, Memory<int>, Memory<long>)> int64Inputs,
            List<(string, Memory<int>, Memory<float>)> floatInputs, string outputName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var shape = int64Inputs[0].Item2.Span;
            Batches.Add((shape[0], shape[1]));
            for (var index = 0; index < shape[0]; index++)
            {
                InputRows.Add(int64Inputs[0].Item3.Slice(index * shape[1], shape[1]));
                if (int64Inputs.Count > 1 && int64Inputs[1].Item1 == "attention_mask")
                    MaskLengths.Add(int64Inputs[1].Item3.Span.Slice(index * shape[1], shape[1]).Count(1L));
            }
            ImageFeatureLengths.Add(floatInputs[0].Item3.Length);
            BeforeInference?.Invoke(Batches.Count);
            var vectors = new float[OutputLength ?? shape[0] * 768];
            for (var index = 0; index < shape[0]; index++) vectors[index * 768] = 1;
            return vectors;
        }
        public void Dispose() => IsDisposed = true;
    }

    private sealed class FixedOcrService(string text) : IOcrService
    {
        public bool IsAvailable => true;
        public Task ReleaseSessionsAsync() => Task.CompletedTask;
        public Task<IReadOnlyList<PluginCore.OcrTextRegion>> RecognizeFileAsync(string imagePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PluginCore.OcrTextRegion>>([new(text, 0, 0, 100, 100)]);
        public Task<IReadOnlyList<PluginCore.OcrTextRegion>> RecognizeAsync(Mat image, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static void AssertVector(JsonElement row, float[] vector, double minimumCosine)
    {
        Assert.HasCount(768, vector);
        var expected = row.GetProperty("vector").EnumerateArray().Select(value => value.GetDouble()).ToArray();
        var norm = Math.Sqrt(vector.Sum(value => (double)value * value));
        var cosine = vector.Select((value, index) => value * expected[index]).Sum() / norm;
        Assert.AreEqual(1d, norm, 1e-6);
        Assert.IsTrue(cosine >= minimumCosine, $"Embedding cosine: {cosine}");
    }
}
